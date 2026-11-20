import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import {
  moneyCents, quantityTenThousandths, unitPriceTenThousandths, qualifyItemization, stableSourceRequestId,
  toCompleteReceiptRequest, getPaperlessText, extractUsingOpenLyric, sanitizePurchaseDraft,
  getFireflyWithdrawals, suggestFireflyLink, normalizeUnit, canonicalUnits, dimensionOfUnit,
  attestationDigest, verifyReviewAttestation, authorizeImport
} from "./receipt-bridge-core.mjs";
import { createAtlasBridge } from "./receipt-bridge-server.mjs";

const ocr = [
  "H-E-B 11/18/2026", "MILK 1 3.99", "EGGS 1 5.00",
  "COUPON -1.00", "TAX 0.61", "TOTAL 8.60"
].join("\n");
const good = {
  merchant: "H-E-B", date: "2026-11-18", currency: "USD",
  merchantEvidence: "H-E-B 11/18/2026", dateEvidence: "11/18/2026", totalEvidence: "TOTAL 8.60",
  total: "8.60", tax: "0.61", confidence: "high",
  items: [
    { description: "Milk", evidence: "MILK 1 3.99", quantity: "1", unitPrice: "3.99", total: "3.99" },
    { description: "Eggs", evidence: "EGGS 1 5.00", quantity: "1", unitPrice: "5.00", total: "5.00" }
  ],
  adjustments: [{ type: "coupon", amount: "-1.00", description: "COUPON" }]
};
const config = {
  paperlessUrl: "https://paperless.test", paperlessInstance: "atlas-test",
  paperlessToken: "paperless-secret", requiredTagId: 77, blockedTagIds: [5],
  openLyricUrl: "https://oathgate.test/v1", openLyricModel: "Open_Lyric",
  openLyricToken: "llm-secret", receiptsUrl: "https://receipts.test",
  receiptsApiKey: "receipts-secret",
  fireflyUrl: "https://firefly.test", fireflyToken: "firefly-secret",
  webhookSecret: "hook-secret", operatorSecret: "operator-secret",
  allowCloudProcessing: true
};
function json(data, status=200) {
  return new Response(JSON.stringify(data), { status, headers: { "content-type": "application/json" } });
}
function mock({ document = {}, draft = good, fireflyPages = null } = {}) {
  const calls = [];
  let doc = { id: 44, tags: [77], modified: "2026-11-18T15:00:00Z", content: ocr, ...document };
  const fetcher = async (target, options = {}) => {
    const url = new URL(target); calls.push({ host: url.host, pathname: url.pathname, method: options.method, headers: options.headers, body: options.body });
    if (url.host === "paperless.test") return json(doc);
    if (url.host === "oathgate.test") return json({ choices: [{message: {content: JSON.stringify(draft)}}] });
    if (url.host === "receipts.test") return json({ receipt: { id: "receipts-101" } });
    if (url.host === "firefly.test") {
      const page = Number(url.searchParams.get("page") || "1");
      return json(fireflyPages?.[page - 1] ?? { data: [], links: { next: null } });
    }
    throw new Error("Unexpected host: " + url.host);
  };
  return { fetcher, calls, setDocument: x => { doc = { ...doc, ...x }; } };
}
const fireflyOne = {
  id: "31",
  attributes: {
    transactions: [{
      type: "withdrawal", date: "2026-11-18T15:00:00Z", amount: "8.60",
      currency_code: "USD", source_id: "9", destination_name: "HEB"
    }]
  }
};
const fireflySnapshot = { data: [fireflyOne], links: { next: null }, meta: { pagination: { current_page: 1, total_pages: 1 } } };

test("money and quantity preserve exact decimals without JS float rounding", () => {
  assert.equal(moneyCents("3.99"), 399n);
  assert.equal(moneyCents("-1.00", {allowNegative:true}), -100n);
  assert.equal(quantityTenThousandths("1.3750"), 13750n);
  assert.equal(unitPriceTenThousandths("3.4590"), 34590n);
  assert.throws(() => moneyCents("1.001"));
  assert.throws(() => moneyCents("-1.00"));
});
test("valid receipt with coupon balances exactly, using original OCR evidence", () => {
  const q = qualifyItemization(good, ocr);
  assert.equal(q.ok, true);
  assert.equal(q.total, 8.6);
  assert.equal(q.items.length, 2);
  assert.equal(q.adjustments[0].amount, -1);
});
test("bad OCR line, guessed product, wrong arithmetic, and low confidence are blocked", () => {
  assert.equal(qualifyItemization({...good,items:[{...good.items[0],evidence:"invented item"}]},ocr).ok,false);
  assert.equal(qualifyItemization({...good,total:"8.61"},ocr).ok,false);
  assert.equal(qualifyItemization({...good,merchantEvidence:"UNKNOWN STORE"},ocr).ok,false);
  assert.equal(qualifyItemization({...good,totalEvidence:"TOTAL 0.01"},ocr).ok,false);
  assert.equal(qualifyItemization({...good,confidence:"low"},ocr).ok,false);
  assert.equal(qualifyItemization({...good,items:[{...good.items[0],total:"3.98"}]},ocr).ok,false);
});
test("weighed produce retains four-decimal quantity and unit price", () => {
  const text="PRODUCE TOMATO 1.2500 LB @2.4599 3.07\\nTOTAL 3.07\\n11/18/2026\\nMARKET";
  const raw={
    merchant:"Market",merchantEvidence:"MARKET",
    date:"2026-11-18",dateEvidence:"11/18/2026",
    total:"3.07",totalEvidence:"TOTAL 3.07",
    tax:"0.00",confidence:"high",currency:"USD",
    items:[{description:"Tomato by pound",evidence:"PRODUCE TOMATO 1.2500 LB @2.4599 3.07",
      quantity:"1.2500",unitPrice:"2.4599",total:"3.07"}],
    adjustments:[]
  };
  const result=qualifyItemization(raw,text);
  assert.deepEqual(result.problems,[]);
  assert.equal(result.ok,true);
  assert.equal(result.items[0].quantity,1.25);
  assert.equal(result.items[0].unitPrice,2.4599);
});
test("import payload is idempotent and does not create a Receipts payment transaction", () => {
  const q = qualifyItemization(good,ocr);
  const a=toCompleteReceiptRequest(q,"atlas-test",44);
  const b=toCompleteReceiptRequest(q,"atlas-test",44);
  assert.deepEqual(a,b);
  assert.match(a.clientRequestId,/^[0-9a-f-]{36}$/);
  assert.deepEqual(a.transactions,[]);
  assert.equal(toCompleteReceiptRequest(q,"other-instance",44).clientRequestId === a.clientRequestId,false);
});
test("Paperless reader fetches only JSON OCR content, checks tag and refuses blocked tags", async () => {
  const m=mock();
  const r=await getPaperlessText(m.fetcher,config,44);
  assert.equal(r.id,44);
  assert.equal(typeof r.contentHash,"string");
  assert.equal(m.calls[0].pathname,"/api/documents/44/");
  assert.equal(m.calls[0].headers.Authorization,"Token paperless-secret");
  assert.equal(m.calls[0].method,"GET");
  await assert.rejects(getPaperlessText(mock({document:{tags:[5,77]}}).fetcher,config,44),/excluded/);
  await assert.rejects(getPaperlessText(mock({document:{tags:[1]}}).fetcher,config,44),/tag absent/);
});
test("model processing is explicit opt-in and rejects markdown wrappers", async () => {
  await assert.rejects(extractUsingOpenLyric(mock().fetcher,{...config,allowCloudProcessing:false},ocr),/disabled/);
  const result=await extractUsingOpenLyric(mock().fetcher,config,ocr);
  assert.equal(result.items.length,2);
  await assert.rejects(extractUsingOpenLyric(mock({draft:{...good}}).fetcher,{...config,openLyricUrl:"file:///etc"},ocr),/Invalid configured/);
});
test("Firefly fetch retrieves every page read-only",async()=>{
  const p1={data:[fireflyOne],links:{next:"https://firefly.test/api/v1/transactions?page=2"}};
  const p2={data:[{...fireflyOne,id:"32"}],links:{next:null}};
  const m=mock({fireflyPages:[p1,p2]});
  const s=await getFireflyWithdrawals(m.fetcher,config,"2026-11-16","2026-11-18");
  assert.equal(s.data.length,2);
  assert.equal(m.calls.filter(x=>x.host==="firefly.test").length,2);
  assert.ok(m.calls.filter(x=>x.host==="firefly.test").every(x=>x.method==="GET"));
  assert.equal(suggestFireflyLink(qualifyItemization(good,ocr),44,"9",fireflySnapshot).status,"PROPOSED_MATCH");
  assert.equal(suggestFireflyLink(qualifyItemization(good,ocr),44,"",fireflySnapshot).status,"REVIEW");
});
test("Firefly cross-origin pagination is rejected without leaking token",async()=>{
  const m=mock({fireflyPages:[{data:[],links:{next:"https://evil.test/api/v1/transactions?page=2"}}]});
  await assert.rejects(getFireflyWithdrawals(m.fetcher,config,"2026-11-16","2026-11-18"),/changed origin/);
  assert.equal(m.calls.length,1);
});
async function exerciseBridge(fn,{draft=good,document={},fireflyPages,overrides={}}={}) {
  const dir=mkdtempSync(join(tmpdir(),"atlas-bridge-test-"));
  const m=mock({draft,document,fireflyPages});
  const bridge=createAtlasBridge({...config,...overrides},{fetcher:m.fetcher,dataDir:dir});
  await new Promise(resolve=>bridge.server.listen(0,"127.0.0.1",resolve));
  const port=bridge.server.address().port;
  const url="http://127.0.0.1:"+port;
  try { await fn({url,bridge,...m}); }
  finally {
    await new Promise(resolve=>bridge.server.close(resolve));
    rmSync(dir,{recursive:true,force:true});
  }
}
async function untilReady(bridge,id) {
  for(let i=0;i<100;i++){
    const s=bridge.state(id);
    if(s?.status && !["QUEUED"].includes(s.status))return s;
    await new Promise(resolve=>setTimeout(resolve,4));
  }
  throw new Error("Bridge failed to process synthetic webhook");
}
async function http(url,method,body,header,secret) {
  return fetch(url,{method,headers:{"Content-Type":"application/json",[header]:secret},body:body?JSON.stringify(body):undefined});
}
test("Paperless webhook requires explicit secret; stores preview without premature Receipt write",async()=>{
  await exerciseBridge(async({url,bridge,calls})=>{
    const denied=await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key","bad");
    assert.equal(denied.status,401);
    const accepted=await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    assert.equal(accepted.status,202);
    const state=await untilReady(bridge,44);
    assert.equal(state.status,"READY_FOR_REVIEW");
    assert.equal(state.draft.items.length,2);
    assert.equal(calls.some(c=>c.host==="receipts.test"),false);
    const secureRead=await http(url+"/drafts/44","GET",null,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(secureRead.status,200);
    assert.equal((await secureRead.json()).paperlessId,44);
  });
});
test("explicit review imports to Receipts, replay is idempotent and never writes Firefly",async()=>{
  await exerciseBridge(async({url,bridge,calls})=>{
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const first=await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(first.status,200);
    const second=await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(second.status,200);
    assert.equal(bridge.state(44).receiptId,"receipts-101");
    assert.equal(calls.filter(c=>c.host==="receipts.test").length,1);
    assert.equal(calls.some(c=>c.host==="firefly.test"&&c.method!=="GET"),false);
    const p=JSON.parse(calls.find(c=>c.host==="receipts.test").body);
    assert.equal(p.transactions.length,0);
  });
});
test("source OCR revision changing after preview blocks imports",async()=>{
  await exerciseBridge(async({url,bridge,setDocument,calls})=>{
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    setDocument({content:ocr+"\nADDITIONAL CHARGE 3.00"});
    const out=await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(out.status,409);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length,0);
  });
});
test("Firefly match endpoint cannot mutate ledger and requires a reviewed source account",async()=>{
  await exerciseBridge(async({url,bridge,calls})=>{
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const resp=await http(url+"/drafts/44/match","POST",{fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(resp.status,200);
    assert.equal((await resp.json()).status,"UNMATCHED");
    assert.ok(calls.filter(c=>c.host==="firefly.test").every(c=>c.method==="GET"));
  });
});

test("unexpected model fields cannot survive into persisted purchase drafts", async () => {
  await exerciseBridge(async ({url, bridge}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    const state = await untilReady(bridge,44);
    assert.equal(state.draft.accountNumber,undefined);
    assert.equal(state.draft.cardNumber,undefined);
    assert.equal(state.draft.items[0].cardNumber,undefined);
    const edited = {...state.draft, cardNumber: "4111111111111111", expectedRevision: state.draftRevision};
    const saved = await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(saved.status,200);
    assert.equal(bridge.state(44).draft.cardNumber,undefined);
  },{draft:{...good,accountNumber:"bank-secret", cardNumber:"4111111111111111",
    items:[{...good.items[0],cardNumber:"secret"},good.items[1]]}});
});
test("explicit opt-in imports only high-quality evidence with no Firefly writes",async()=>{
  await exerciseBridge(async({url,bridge,calls})=>{
    const webhook=await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    assert.equal(webhook.status,202);
    const state=await untilReady(bridge,44);
    assert.equal(state.status,"IMPORTED");
    assert.equal(calls.filter(c=>c.host==="receipts.test"&&c.method==="POST").length,1);
    assert.equal(calls.filter(c=>c.host==="firefly.test").length,0);
  },{overrides:{autoImport:true}});
});
test("auto import never writes a low-confidence receipt",async()=>{
  await exerciseBridge(async({url,bridge,calls})=>{
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    const state=await untilReady(bridge,44);
    assert.equal(state.status,"REVIEW_REQUIRED");
    assert.equal(calls.some(c=>c.host==="receipts.test"),false);
  },{overrides:{autoImport:true},draft:{...good,confidence:"low"}});
});

test("Paperless draft inbox lists only bounded authenticated source summaries", async () => {
  await exerciseBridge(async ({url, bridge}) => {
    const denied = await http(url+"/drafts","GET",null,"x-atlas-operator-key","wrong");
    assert.equal(denied.status,401);
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const allowed = await http(url+"/drafts","GET",null,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(allowed.status,200);
    const payload=await allowed.json();
    assert.equal(payload.drafts.length,1);
    assert.equal(payload.drafts[0].paperlessId,44);
    assert.equal(payload.drafts[0].status,"READY_FOR_REVIEW");
    assert.equal("content" in payload.drafts[0],false);
  });
});
test("malformed request bodies fail with 400, not a generic 422", async () => {
  await exerciseBridge(async ({url, bridge}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const raw = (path, body) => fetch(url+path, { method: "PUT", headers: { "Content-Type": "application/json", "x-atlas-operator-key": config.operatorSecret }, body });
    const badPut = await raw("/drafts/44", "{not json");
    assert.equal(badPut.status, 400);
    assert.match(await badPut.text(), /Malformed/);
    const badHook = await fetch(url+"/hooks/paperless", { method: "POST", headers: { "Content-Type": "application/json", "x-atlas-hook-key": config.webhookSecret }, body: "{{{" });
    assert.equal(badHook.status, 400);
    const badImport = await fetch(url+"/drafts/44/import", { method: "POST", headers: { "Content-Type": "application/json", "x-atlas-operator-key": config.operatorSecret }, body: "[oops" });
    assert.equal(badImport.status, 400);
    const badId = await http(url+"/hooks/paperless","POST",{sourceId:"nope"},"x-atlas-hook-key",config.webhookSecret);
    assert.equal(badId.status, 400);
  });
});
test("reviewed draft revision rejects stale approvals", async () => {
  await exerciseBridge(async ({url, bridge, calls}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    assert.equal(bridge.state(44).draftRevision, 1);
    const edited = { ...bridge.state(44).draft, merchant: "H-E-B Plus", expectedRevision: bridge.state(44).draftRevision };
    const saved = await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(saved.status, 200);
    assert.equal((await saved.json()).draftRevision, 2);
    const stale = await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(stale.status, 409);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 0);
    const fresh = await http(url+"/drafts/44/import","POST",{expectedRevision:2},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(fresh.status, 200);
    const replay = await http(url+"/drafts/44/import","POST",{expectedRevision:2},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(replay.status, 200);
    assert.equal((await replay.json()).receiptId, "receipts-101");
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 1);
  });
});
test("concurrent approvals cannot create duplicate purchases", async () => {
  await exerciseBridge(async ({url, bridge, calls}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const rev = bridge.state(44).draftRevision;
    const [first, second] = await Promise.all([bridge.importReviewed(44, rev), bridge.importReviewed(44, rev)]);
    const ok = [first, second].filter(r => r.status === 200);
    assert.equal(ok.length >= 1, true);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 1);
    for (const r of ok) assert.equal(r.result.receiptId, "receipts-101");
    assert.equal(bridge.state(44).receiptId, "receipts-101");
  });
});
test("reviewer corrections survive OCR changes until re-review", async () => {
  await exerciseBridge(async ({url, bridge, calls, setDocument}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const edited = { ...bridge.state(44).draft, merchant: "H-E-B Plus", expectedRevision: bridge.state(44).draftRevision };
    await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    setDocument({ content: ocr+"\nADDITIONAL CHARGE 3.00" });
    await bridge.processDocument(44);
    const kept = bridge.state(44);
    assert.equal(kept.status, "SOURCE_CHANGED_REVIEW");
    assert.equal(kept.draft.merchant, "H-E-B Plus");
    assert.equal(kept.draftRevision, 2);
    const blocked = await http(url+"/drafts/44/import","POST",{expectedRevision:2},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(blocked.status, 409);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 0);
  });
});
test("confirmed payment links store durably without touching the ledger", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, calls}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const missing = await http(url+"/drafts/44/confirm-match","POST",{fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(missing.status, 400);
    const wrong = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"32",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(wrong.status, 409);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    assert.equal((await confirmed.json()).status, "MATCH_CONFIRMED");
    const stored = await http(url+"/drafts/44","GET",null,"x-atlas-operator-key",config.operatorSecret);
    assert.equal((await stored.json()).confirmedFireflyTransactionId, "31");
    assert.equal(calls.some(c=>c.host==="firefly.test"&&c.method!=="GET"), false);
    assert.equal(calls.some(c=>c.host==="receipts.test"), false);
  }, { fireflyPages: pages });
});

test("unit spellings normalize to the canonical catalog without guessing", () => {
  assert.equal(normalizeUnit("GAL"), "gal");
  assert.equal(normalizeUnit(" gallons "), "gal");
  assert.equal(normalizeUnit("Lbs"), "lb");
  assert.equal(normalizeUnit("L"), "l");
  assert.equal(normalizeUnit("fl_oz"), "fl_oz");
  assert.equal(normalizeUnit(""), null);
  assert.equal(normalizeUnit(null), null);
  assert.equal(normalizeUnit("litre"), "l");
  assert.equal(normalizeUnit("dozen"), undefined);
  assert.equal(normalizeUnit("scoop"), undefined);
  assert.ok(canonicalUnits.has("gal") && canonicalUnits.has("g") && canonicalUnits.has("ml") && canonicalUnits.has("each"));
});
test("recognized units flow to the import payload with the Paperless source", () => {
  const withUnit = { ...good, items: [{ ...good.items[0], unit: "GAL" }, good.items[1]] };
  const q = qualifyItemization(withUnit, ocr);
  assert.equal(q.ok, true);
  assert.equal(q.items[0].unitOfMeasure, "gal");
  assert.equal(q.items[1].unitOfMeasure, null);
  const payload = toCompleteReceiptRequest(q, "atlas-test", 44);
  assert.equal(payload.items[0].unitOfMeasure, "gal");
  assert.equal(payload.receipt.sourcePaperlessDocumentId, 44);
});
test("unrecognized units require review and never silently import", async () => {
  const bad = { ...good, items: [{ ...good.items[0], unit: "dozen" }, good.items[1]] };
  assert.equal(qualifyItemization(bad, ocr).ok, false);
  assert.ok(qualifyItemization(bad, ocr).problems.includes("Unit needs review"));
  await exerciseBridge(async ({ url, bridge, calls }) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const edited = { ...bridge.state(44).draft, expectedRevision: bridge.state(44).draftRevision, items: [{ ...bridge.state(44).draft.items[0], unit: "scoop" }] };
    const saved = await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(saved.status, 200);
    const out = await http(url+"/drafts/44/import","POST",{expectedRevision:2},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(out.status, 422);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 0);
  });
});
test("one Firefly payment cannot back two receipts; own replay stays idempotent", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, setDocument}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const first = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(first.status, 200);
    // Second receipt for the same purchase: switch the mocked Paperless doc id.
    setDocument({id:45});
    await http(url+"/hooks/paperless","POST",{sourceId:45},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,45);
    const dup = await http(url+"/drafts/45/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(dup.status, 409);
    assert.equal((await dup.json()).claimedByPaperlessId, 44);
    assert.equal(bridge.state(45).confirmedFireflyTransactionId, undefined);
    // Replaying receipt 44's own confirmation is still idempotent 200.
    setDocument({id:44});
    const replay = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(replay.status, 200);
    assert.equal((await replay.json()).status, "MATCH_CONFIRMED");
  }, { fireflyPages: pages });
});
test("draft saves require the edited revision; stale saves are rejected", async () => {
  await exerciseBridge(async ({url, bridge}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const missing = await http(url+"/drafts/44","PUT",{...bridge.state(44).draft},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(missing.status, 400);
    const stale = await http(url+"/drafts/44","PUT",{...bridge.state(44).draft, expectedRevision:99},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(stale.status, 409);
    const current = await http(url+"/drafts/44","PUT",{...bridge.state(44).draft, merchant:"H-E-B Plus", expectedRevision:bridge.state(44).draftRevision},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(current.status, 200);
    assert.equal((await current.json()).draftRevision, 2);
    assert.equal(bridge.state(44).draft.merchant, "H-E-B Plus");
  });
});
test("import approvals require the reviewed revision", async () => {
  await exerciseBridge(async ({url, bridge, calls}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const missing = await http(url+"/drafts/44/import","POST",{},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(missing.status, 400);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 0);
    const fresh = await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(fresh.status, 200);
  });
});
test("package content validates: measurable per package, countable unit", () => {
  assert.equal(dimensionOfUnit("gal"), "volume");
  assert.equal(dimensionOfUnit("lb"), "mass");
  assert.equal(dimensionOfUnit("each"), "count");
  assert.equal(dimensionOfUnit("scoop"), undefined);
  const carton = {...good, items:[{...good.items[0], unit:"each", packageContentQuantity:"64", packageContentUnit:"fl_oz"}, good.items[1]]};
  const q = qualifyItemization(carton, ocr);
  assert.equal(q.ok, true);
  assert.equal(q.items[0].packageContentQuantity, 64);
  assert.equal(q.items[0].packageContentUnit, "fl_oz");
  const payload = toCompleteReceiptRequest(q, "atlas-test", 44);
  assert.equal(payload.items[0].packageContentQuantity, 64);
  assert.equal(payload.items[0].packageContentUnit, "fl_oz");
  const badQty = {...good, items:[{...good.items[0], unit:"each", packageContentQuantity:"lots", packageContentUnit:"fl_oz"}]};
  assert.ok(qualifyItemization(badQty, ocr).problems.includes("Package content quantity requires review"));
  const massUnit = {...good, items:[{...good.items[0], unit:"fl_oz", packageContentQuantity:"64", packageContentUnit:"fl_oz"}]};
  assert.ok(qualifyItemization(massUnit, ocr).problems.includes("Unit must count packages when package content is set"));
  const badUnit = {...good, items:[{...good.items[0], unit:"each", packageContentQuantity:"64", packageContentUnit:"scoop"}]};
  assert.ok(qualifyItemization(badUnit, ocr).problems.includes("Package content unit requires review"));
  const half = {...good, items:[{...good.items[0], unit:"each", packageContentQuantity:"64"}]};
  assert.ok(qualifyItemization(half, ocr).problems.includes("Package content unit requires review"));
});
test("count-based multipack preserves purchase quantity separately from package contents", () => {
  // EGGS 12 CT $5.00 in the shared fixture: purchased quantity 1 carton,
  // contents 12 count, package price $5.00. Quantity and contents
  // must never be confused.
  const localOcr = ["H-E-B 11/18/2026", "MILK 1 3.99", "EGGS 1 5.00", "TAX 0.61", "TOTAL 9.60"].join("\n");
  const multipack = {...good, tax:"0.61", adjustments: [], total:"9.60", totalEvidence:"TOTAL 9.60", items:[{...good.items[0], description:"EGGS 12 CT", evidence:"EGGS 1 5.00", quantity:"1", unitPrice:"5.00", total:"5.00", unit:"each", packageContentQuantity:"12", packageContentUnit:"count"}, {...good.items[1], evidence:"MILK 1 3.99", quantity:"1", unitPrice:"3.99", total:"3.99"}]};
  const q = qualifyItemization(multipack, localOcr);
  assert.equal(q.ok, true);
  assert.equal(q.items[0].quantity, 1);
  assert.equal(q.items[0].unitPrice, 5.00);
  assert.equal(q.items[0].unitOfMeasure, "each");
  assert.equal(q.items[0].packageContentQuantity, 12);
  assert.equal(q.items[0].packageContentUnit, "count");
  const payload = toCompleteReceiptRequest(q, "atlas-test", 44);
  assert.equal(payload.items[0].quantity, 1);
  assert.equal(payload.items[0].packageContentQuantity, 12);
  assert.equal(payload.items[0].packageContentUnit, "count");
});
test("missing purchase quantity requires review instead of assuming one", () => {
  const base = {...good.items[0]};
  delete base.quantity;
  const missing = {...good, items:[base, good.items[1]]};
  const q = qualifyItemization(missing, ocr);
  assert.equal(q.ok, false);
  assert.ok(q.problems.includes("Purchase quantity requires review"));
  // The sanitizer must preserve the unknown instead of inventing a count.
  const sanitized = sanitizePurchaseDraft(missing);
  assert.equal(sanitized.items[0].quantity, "");
});
test("editing a confirmed draft voids the payment confirmation", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    assert.equal(bridge.state(44).confirmedFireflyTransactionId, "31");
    const edited = { ...bridge.state(44).draft, merchant: "H-E-B Plus", expectedRevision: bridge.state(44).draftRevision };
    const saved = await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    assert.equal(saved.status, 200);
    assert.equal((await saved.json()).confirmationInvalidated, true);
    assert.equal(bridge.state(44).confirmedFireflyTransactionId, null);
    const reread = await http(url+"/drafts/44","GET",null,"x-atlas-operator-key",config.operatorSecret);
    assert.equal((await reread.json()).confirmedFireflyTransactionId, null);
  }, { fireflyPages: pages });
});
test("OCR change after confirmation forces re-review without the old link", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, setDocument}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    // Description-only edit: reviewerEdited without disturbing the payment match.
    const items = [{ ...bridge.state(44).draft.items[0], description: "Milk 2 percent" }, bridge.state(44).draft.items[1]];
    const edited = { ...bridge.state(44).draft, items, expectedRevision: bridge.state(44).draftRevision };
    await http(url+"/drafts/44","PUT",edited,"x-atlas-operator-key",config.operatorSecret);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    setDocument({ content: ocr+"\nADDITIONAL CHARGE 3.00" });
    await bridge.processDocument(44);
    const kept = bridge.state(44);
    assert.equal(kept.status, "SOURCE_CHANGED_REVIEW");
    assert.equal(kept.confirmedFireflyTransactionId, null);
  }, { fireflyPages: pages });
});
test("post-import OCR change voids the confirmed link", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, setDocument, calls}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    const imported = await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(imported.status, 200);
    assert.equal(bridge.state(44).confirmedFireflyTransactionId, "31");
    setDocument({ content: ocr+"\nADDITIONAL CHARGE 3.00" });
    await bridge.processDocument(44);
    const kept = bridge.state(44);
    assert.equal(kept.status, "SOURCE_CHANGED_REVIEW");
    assert.equal(kept.receiptId, "receipts-101");
    assert.equal(kept.confirmedFireflyTransactionId, null);
    assert.equal(calls.filter(c=>c.host==="receipts.test").length, 1);
  }, { fireflyPages: pages });
});
test("blocked import approval voids the stale payment link", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, setDocument}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    setDocument({ content: ocr+"\nADDITIONAL CHARGE 3.00" });
    const blocked = await http(url+"/drafts/44/import","POST",{expectedRevision:1},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(blocked.status, 409);
    assert.equal(bridge.state(44).confirmedFireflyTransactionId, null);
    assert.equal(bridge.state(44).status, "SOURCE_CHANGED_REVIEW");
  }, { fireflyPages: pages });
});
test("blocked payment match voids the stale stored link", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  await exerciseBridge(async ({url, bridge, setDocument}) => {
    await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
    await untilReady(bridge,44);
    const confirmed = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(confirmed.status, 200);
    setDocument({ content: ocr+"\nADDITIONAL CHARGE 3.00" });
    const blocked = await http(url+"/drafts/44/match","POST",{fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
    assert.equal(blocked.status, 409);
    assert.equal(bridge.state(44).confirmedFireflyTransactionId, null);
    assert.equal(bridge.state(44).status, "SOURCE_CHANGED_REVIEW");
  }, { fireflyPages: pages });
});
test("edit landing while Firefly responds blocks the stale confirmation", async () => {
  const pages = [{ data: [fireflyOne], links: { next: null } }];
  const dir = mkdtempSync(join(tmpdir(), "atlas-bridge-race-"));
  try {
    const m = mock({ fireflyPages: pages });
    let hookArmed = true;
    const fetcher = async (target, options = {}) => {
      const url = new URL(target);
      if (url.host === "firefly.test" && hookArmed) {
        hookArmed = false;
        // A reviewer save landing while Firefly responds: bump the revision
        // at the state level, exactly as PUT would (merchant corrected).
        const f = join(dir, "paperless-44.json");
        const state = JSON.parse(readFileSync(f, "utf8"));
        state.draft = { ...state.draft, merchant: "OTHER MART" };
        state.draftRevision = (state.draftRevision ?? 0) + 1;
        state.reviewerEdited = true;
        state.confirmedFireflyTransactionId = null;
        state.confirmedAt = null;
        writeFileSync(f, JSON.stringify(state, null, 2));
      }
      return m.fetcher(target, options);
    };
    const bridge = createAtlasBridge({ ...config }, { fetcher, dataDir: dir });
    await new Promise(resolve => bridge.server.listen(0, "127.0.0.1", resolve));
    const url = "http://127.0.0.1:" + bridge.server.address().port;
    try {
      await http(url+"/hooks/paperless","POST",{sourceId:44},"x-atlas-hook-key",config.webhookSecret);
      await untilReady(bridge,44);
      assert.equal(bridge.state(44).draftRevision, 1);
      const out = await http(url+"/drafts/44/confirm-match","POST",{fireflyTransactionId:"31",fireflySourceAccountId:"9"},"x-atlas-operator-key",config.operatorSecret);
      assert.equal(out.status, 409);
      assert.match(await out.text(), /changed while checking/);
      assert.equal(bridge.state(44).draftRevision, 2);
      assert.equal(bridge.state(44).confirmedFireflyTransactionId, null);
    } finally {
      await new Promise(resolve => bridge.server.close(resolve));
    }
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test("low-confidence import requires a bound, non-proposer approval", () => {
  const low = qualifyItemization({ ...good, confidence: "low" }, ocr);
  assert.equal(low.ok, false);
  assert.deepEqual(low.problems, ["Model did not attest high confidence"]);
  const digest = attestationDigest(ocr);
  const valid = { reviewer: "jayson", approvedAt: "2026-11-20T06:30:00Z", ocrSha256: digest, draftRevision: "4" };
  const context = { ocrText: ocr, draftRevision: "4", proposer: "open-lyric" };

  assert.equal(authorizeImport(low, null, context).ok, false);
  assert.match(authorizeImport(low, null, context).problems.join(" "), /approval is required/i);
  assert.match(authorizeImport(low, { ...valid, reviewer: "Open-Lyric" }, context).problems.join(" "), /differ from the proposer/);
  assert.match(authorizeImport(low, { ...valid, ocrSha256: attestationDigest(ocr + "\nEXTRA") }, context).problems.join(" "), /source OCR changed/);
  assert.match(authorizeImport(low, { ...valid, draftRevision: "3" }, context).problems.join(" "), /draft revision changed/);
  assert.match(authorizeImport(low, { ...valid, approvedAt: "not-a-date" }, context).problems.join(" "), /timestamp/);

  const approved = authorizeImport(low, valid, context);
  assert.equal(approved.ok, true);
  assert.equal(approved.approved, true);
  assert.equal(approved.attestation.reviewer, "jayson");
  assert.equal(approved.attestation.draftRevision, "4");
  assert.equal(approved.attestation.ocrSha256, digest);

  const high = qualifyItemization({ ...good, total: "8.61" }, ocr);
  assert.equal(authorizeImport(high, valid, context).ok, false, "structural findings stay unimportable");

  const payload = toCompleteReceiptRequest(low, "paperless", 25, approved.attestation);
  assert.equal(payload.reviewAttestation.reviewer, "jayson");
  assert.equal(payload.receipt.sourcePaperlessDocumentId, 25);
  assert.equal(toCompleteReceiptRequest(qualifyItemization(good, ocr), "paperless", 25).reviewAttestation, null);
  assert.equal(verifyReviewAttestation(valid, context).ok, true);
});
test("low-confidence draft imports only with an authenticated, draft-bound approval", async () => {
  await exerciseBridge(async ({ url, bridge, calls }) => {
    await http(url + "/hooks/paperless", "POST", { sourceId: 44 }, "x-atlas-hook-key", config.webhookSecret);
    await untilReady(bridge, 44);
    const edited = { ...bridge.state(44).draft, confidence: "low", expectedRevision: bridge.state(44).draftRevision };
    const saved = await http(url + "/drafts/44", "PUT", edited, "x-atlas-operator-key", config.operatorSecret);
    assert.equal(saved.status, 200);
    const revision = bridge.state(44).draftRevision;
    const digest = createHash("sha256").update(ocr, "utf8").digest("hex");
    const post = (body, reviewer) => fetch(url + "/drafts/44/import", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-atlas-operator-key": config.operatorSecret,
        ...(reviewer ? { "x-atlas-reviewer": reviewer } : {})
      },
      body: JSON.stringify(body)
    });

    const blocked = await post({ expectedRevision: revision });
    assert.equal(blocked.status, 422);
    assert.match((await blocked.json()).problems.join(" "), /approval is required/i);
    assert.equal(calls.some(c => c.host === "receipts.test"), false);

    const stale = await post({ expectedRevision: revision, review: { approvedAt: "2026-11-20T06:30:00Z", ocrSha256: createHash("sha256").update(ocr + "\nX").digest("hex"), draftRevision: String(revision) } }, "jayson");
    assert.equal(stale.status, 422);
    assert.match((await stale.json()).problems.join(" "), /source OCR changed/);

    const approved = await post({ expectedRevision: revision, review: { approvedAt: "2026-11-20T06:30:00Z", ocrSha256: digest, draftRevision: String(revision) } }, "jayson");
    assert.equal(approved.status, 200);
    assert.equal((await approved.json()).status, "IMPORTED");
    const payload = JSON.parse(calls.find(c => c.host === "receipts.test").body);
    assert.equal(payload.reviewAttestation.reviewer, "jayson");
    assert.equal(payload.reviewAttestation.ocrSha256, digest);
    assert.equal(payload.reviewAttestation.draftRevision, String(revision));
  });
});

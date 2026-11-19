import type { components } from "@/generated/api";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useLocation, useNavigate } from "react-router";
import { renderWithProviders } from "@/test/test-utils";
import { mockQueryResult } from "@/test/mock-hooks";
import { usePurchaseSearch } from "@/hooks/usePurchaseSearch";
import { useCategoryTaxonomy } from "@/hooks/useCategoryTaxonomy";
import { taxonomyFixture } from "@/test/taxonomy-fixture";
import PurchaseSearch from "./PurchaseSearch";
import { chooseCategoryOption } from "@/test/category-picker";
vi.mock("@/hooks/usePurchaseSearch", () => ({ usePurchaseSearch: vi.fn() }));
vi.mock("@/hooks/useCategoryTaxonomy", () => ({
  useCategoryTaxonomy: vi.fn(),
}));
const search = vi.mocked(usePurchaseSearch);
const taxonomy = vi.mocked(useCategoryTaxonomy);
const item: components["schemas"]["PurchaseSearchItem"] = {
  id: "item-1",
  receiptId: "receipt-1",
  categoryState: "unclassified",
  categoryPath: [],
  description: "Whole milk",
  receiptItemCode: "MILK-42",
  normalizedDescription: "Milk",
  normalizationReviewState: "verified",
  date: "2020-01-15",
  location: "Old store",
  quantity: 2,
  unitPrice: 1.2345,
  unitPriceCurrency: "USD",
  totalAmount: 2.47,
  totalAmountCurrency: "USD",
};
function Location() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output aria-label="Current URL">{location.search}</output>
      <button onClick={() => navigate(-1)}>Browser Back</button>
    </>
  );
}
function renderPage(route = "/search?q=milk") {
  return renderWithProviders(
    <>
      <PurchaseSearch />
      <Location />
    </>,
    { route },
  );
}
beforeEach(() => {
  localStorage.clear();
  search.mockReset();
  taxonomy.mockReturnValue(
    mockQueryResult({
      isPending: false,
      isLoading: false,
      data: taxonomyFixture,
    }),
  );
  search.mockReturnValue(
    mockQueryResult({
      isPending: false,
      data: {
        categoryView: "asClassified",
        assignmentSelection: "currentPointer",
        items: [item],
        total: 26,
        offset: 0,
        limit: 25,
      },
    }),
  );
});

function currentParams() {
  return new URLSearchParams(
    screen.getByLabelText("Current URL").textContent ?? "",
  );
}

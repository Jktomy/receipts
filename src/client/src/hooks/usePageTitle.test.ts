import { renderHook } from "@testing-library/react";
import { usePageTitle } from "./usePageTitle";

describe("usePageTitle", () => {
  const originalTitle = document.title;

  afterEach(() => {
    document.title = originalTitle;
  });

  it("sets document.title with suffix", () => {
    renderHook(() => usePageTitle("Accounts"));
    expect(document.title).toBe("Accounts - Tracer");
  });

  it("sets document.title to 'Tracer' when title is empty", () => {
    renderHook(() => usePageTitle(""));
    expect(document.title).toBe("Tracer");
  });

  it("updates title when prop changes", () => {
    const { rerender } = renderHook(({ title }) => usePageTitle(title), {
      initialProps: { title: "Accounts" },
    });
    expect(document.title).toBe("Accounts - Tracer");

    rerender({ title: "Receipts List" });
    expect(document.title).toBe("Receipts List - Tracer");
  });
});

import { describe, expect, it } from "vitest";
import { APP_NAME, formatPageTitle } from "./branding";

describe("Tracer branding", () => {
  it("names the application Tracer", () => {
    expect(APP_NAME).toBe("Tracer");
  });

  it("formats page titles with the Tracer suffix", () => {
    expect(formatPageTitle("Dashboard")).toBe("Dashboard - Tracer");
  });

  it("falls back to the bare Tracer name", () => {
    expect(formatPageTitle("")).toBe("Tracer");
  });
});

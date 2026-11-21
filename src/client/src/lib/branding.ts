export const APP_NAME = "Tracer";

export function formatPageTitle(title: string): string {
  return title ? `${title} - ${APP_NAME}` : APP_NAME;
}

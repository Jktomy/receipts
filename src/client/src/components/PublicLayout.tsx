import { Link, Outlet } from "react-router";
import { ThemeToggle } from "@/components/ThemeToggle";
import { APP_NAME } from "@/lib/branding";

export function PublicLayout() {
  return (
    <div className="auth-shell">
      <header className="auth-topbar">
        <Link to="/" className="brand">
          <div className="mark">T</div>
          <div className="name">{APP_NAME}</div>
        </Link>
        <ThemeToggle />
      </header>
      <main className="auth-main">
        <Outlet />
      </main>
    </div>
  );
}

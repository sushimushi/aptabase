import { AppsProvider } from "@features/apps";
import { AuthProvider } from "@features/auth";
import { createBrowserRouter } from "react-router-dom";
import { Toaster } from "sonner";
import { AuthenticationLayout, ConsoleLayout } from "./layout";

const router = createBrowserRouter([
  {
    path: "/auth",
    element: <AuthenticationLayout />,
    children: [
      {
        index: true,
        lazy: () => import("./features/auth/LoginPage"),
      },
      {
        path: "register",
        lazy: () => import("./features/auth/RegisterPage"),
      },
    ],
  },
  {
    path: "/",
    element: (
      <>
        <AuthProvider>
          <AppsProvider>
            <ConsoleLayout />
          </AppsProvider>
        </AuthProvider>
        <Toaster className="hidden lg:block" position="top-right" />
        <Toaster className="block lg:hidden" position="bottom-center" />
      </>
    ),
    children: [
      {
        index: true,
        lazy: () => import("./features/apps/HomePage"),
      },
      {
        path: "/:id",
        lazy: () => import("./features/apps/DashboardPage"),
      },
      {
        path: "/:id/instructions",
        lazy: () => import("./features/apps/InstructionsPage"),
      },
      {
        path: "/:id/settings",
        lazy: () => import("./features/apps/SettingsPage"),
      },
      {
        path: "/billing",
        lazy: () => import("./features/billing/BillingPage"),
      },
    ],
  },
  {},
]);

export default router;

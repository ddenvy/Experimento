import { test, expect } from "@playwright/test";

/**
 * End-to-end tests for the authentication flow.
 * Uses the seeded test user apple@apple.com / Test12345!
 */
const TEST_EMAIL = "apple@apple.com";
const TEST_PASSWORD = "Test12345!";

test.describe("Authentication", () => {
  test("login page renders the sign-in form", async ({ page }) => {
    await page.goto("/login");
    await expect(page.getByRole("heading", { name: "Sign in to Experimento" })).toBeVisible();
    await expect(page.getByLabel("Email")).toBeVisible();
    await expect(page.getByLabel("Password")).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  });

  test("successful login redirects to dashboard", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(TEST_EMAIL);
    await page.getByLabel("Password").fill(TEST_PASSWORD);
    await page.getByRole("button", { name: "Sign in" }).click();

    // Wait for redirect to dashboard
    await expect(page).toHaveURL(/\/$/, { timeout: 10_000 });
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    // Token should be stored
    const token = await page.evaluate(() => localStorage.getItem("access_token"));
    expect(token).toBeTruthy();
  });

  test("invalid credentials show an error message", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(TEST_EMAIL);
    await page.getByLabel("Password").fill("wrong-password");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByText("Invalid credentials.")).toBeVisible();
    // Still on login page
    await expect(page).toHaveURL(/\/login/);
  });

  test("unauthenticated user is redirected to login", async ({ page }) => {
    await page.goto("/");
    // AuthGuard redirects to /login when no token is present
    await expect(page).toHaveURL(/\/login/, { timeout: 10_000 });
  });
});

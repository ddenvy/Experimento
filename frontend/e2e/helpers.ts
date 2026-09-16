import { Page, APIRequestContext } from "@playwright/test";

/**
 * Test credentials for the seeded test user.
 */
export const TEST_USER = {
  email: "apple@apple.com",
  password: "Test12345!",
};

/**
 * Logs in via the backend API and returns the access token.
 * The backend must be reachable at http://localhost:5126.
 */
export async function loginApi(request: APIRequestContext): Promise<string> {
  const resp = await request.post("http://localhost:5126/api/auth/login", {
    data: { email: TEST_USER.email, password: TEST_USER.password },
  });
  if (!resp.ok()) throw new Error(`Login failed: ${resp.status()}`);
  const body = await resp.json();
  return body.accessToken as string;
}

/**
 * Injects the access token into localStorage BEFORE the page loads,
 * so the AuthGuard sees an authenticated state on first render.
 */
export async function injectAuth(page: Page, token: string): Promise<void> {
  await page.addInitScript((t) => {
    localStorage.setItem("access_token", t);
  }, token);
}

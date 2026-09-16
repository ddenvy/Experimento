import { Page, APIRequestContext } from "@playwright/test";

/**
 * Test credentials for the seeded test admin user.
 */
export const TEST_USER = {
  email: "apple@apple.com",
  password: "Test12345!",
};

export interface AuthState {
  token: string;
  cookies: Array<{
    name: string;
    value: string;
    domain?: string;
    path: string;
    expires: number;
    httpOnly: boolean;
    secure: boolean;
    sameSite: "Strict" | "Lax" | "None";
  }>;
}

/**
 * Логинится через API бэкенда и возвращает access-токен вместе с cookie
 * (refresh-токен лежит в httpOnly-cookie).
 * Бэкенд должен быть доступен на http://localhost:5126.
 */
export async function loginApi(request: APIRequestContext): Promise<AuthState> {
  const resp = await request.post("http://localhost:5126/api/auth/login", {
    data: { email: TEST_USER.email, password: TEST_USER.password },
  });
  if (!resp.ok()) throw new Error(`Login failed: ${resp.status()}`);
  const body = await resp.json();

  const storage = await request.storageState();
  return {
    token: body.accessToken as string,
    cookies: storage.cookies as AuthState["cookies"],
  };
}

/**
 * Инъецирует refresh-cookie в браузерный контекст ДО загрузки страницы,
 * чтобы AuthGuard восстановил сессию через /auth/refresh при первом рендере.
 * Access-токен в приложении живёт в памяти и не может быть подставлен извне.
 */
export async function injectAuth(page: Page, cookies: AuthState["cookies"]): Promise<void> {
  await page.context().addCookies(cookies);
}

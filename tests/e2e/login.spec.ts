import { expect, test } from '@playwright/test';

test('muestra acceso y comunica credenciales rechazadas', async ({ page }) => {
  await page.route('**/api/auth/login', route => route.fulfill({ status: 401, json: { detail: 'Credenciales inválidas.' } }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible();
  await page.getByLabel('Usuario', { exact: true }).fill('operador');
  await page.getByLabel('Contraseña', { exact: true }).fill('credencial-invalida');
  await page.getByRole('button', { name: 'Entrar a la plataforma' }).click();
  await expect(page.getByRole('alert')).toHaveText('Credenciales inválidas.');
});

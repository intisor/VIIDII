const { test, expect } = require('@playwright/test');

const baseUrl = process.env.BASE_URL || 'http://localhost:5000';

async function loginAs(page, userLabel) {
  await page.goto(`${baseUrl}/login`);
  await page.getByLabel('Select User').selectOption({ label: userLabel });
  await page.getByRole('button', { name: 'Login' }).click();
}

test('lecturer flow redirects to lecturer home and navigation works', async ({ page }) => {
  await loginAs(page, /Lec001/);
  await page.waitForURL('**/lecturer/home');
  await expect(page.getByText('Lecturer')).toBeVisible();
  await page.getByRole('link', { name: 'Create Session' }).click();
  await page.waitForURL('**/create-session');
  await page.goBack();
  await page.getByRole('link', { name: 'Join Session' }).click();
  await page.waitForURL('**/join-session');
});

test('student flow redirects to student home and join navigation works', async ({ page }) => {
  await loginAs(page, /123456/);
  await page.waitForURL('**/student/home');
  await expect(page.getByText('Student')).toBeVisible();
  await page.getByRole('button', { name: 'Join a Session' }).click();
  await page.waitForURL('**/join-session');
});

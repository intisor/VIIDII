const { test, expect } = require('@playwright/test');

const baseUrl = process.env.BASE_URL || 'http://localhost:5000';

async function loginAs(page, matricNo) {
  await page.goto(`${baseUrl}/login`);
  // Select option by value (matricNo) to be robust against label text changes
  await page.locator('#user-select').selectOption(matricNo);
  // Support both "Login" and "Sign in"
  await page.locator('button[type="submit"], button:has-text("Sign in"), button:has-text("Login")').first().click();
}

test('lecturer flow redirects to lecturer home and navigation works', async ({ page }) => {
  await loginAs(page, 'Lec001');
  await page.waitForURL('**/lecturer/home');
  await expect(page.getByText('Lecturer')).toBeVisible();
  // Support either "Create Session" or "New Session" in a or button
  await page.locator('a:has-text("New Session"), a:has-text("Create Session"), button:has-text("New Session"), button:has-text("Create Session"), #create-session-btn').first().click();
  await page.waitForURL('**/create-session');
  await page.goBack();
  await page.locator('a:has-text("Join Session"), button:has-text("Join Session"), #join-session-btn').first().click();
  await page.waitForURL('**/join-session');
});

test('student flow redirects to student home and join navigation works', async ({ page }) => {
  await loginAs(page, '123456');
  await page.waitForURL('**/student/home');
  await expect(page.getByText('Student')).toBeVisible();
  // Support either "Join a Session" or "Join Session"
  await page.locator('button:has-text("Join Session"), button:has-text("Join a Session")').first().click();
  await page.waitForURL('**/join-session');
});

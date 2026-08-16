import puppeteer from 'puppeteer';

(async () => {
  const browser = await puppeteer.launch({
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox']
  });

  const page = await browser.newPage();
  await page.setViewport({ width: 1600, height: 1000 });
  await page.goto('http://192.168.134.9:5000/?user=manager', { waitUntil: 'networkidle0', timeout: 15000 });

  await new Promise(r => setTimeout(r, 2000));

  // 点击第一条任务行
  const firstRow = await page.$('.workbench-table tbody tr');
  if (firstRow) {
    await firstRow.click();
    await new Promise(r => setTimeout(r, 2500));
  }

  // 截取右侧抽屉
  await page.screenshot({ path: 'C:/Users/yuan2/.gemini/antigravity/brain/29197658-c279-4c19-bd03-7c1aa2fc64bf/live_drawer_view.png', fullPage: true });

  await browser.close();
  console.log('Screenshot saved to live_drawer_view.png');
})();

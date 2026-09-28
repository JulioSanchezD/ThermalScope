// Rasterize the existing vector logo into a Windows ICO; no new visual design is generated.
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : 'playwright');
const svg = await readFile(resolve('wwwroot/icon.svg'), 'utf8');
const browser = await chromium.launch();
try {
  const images = [];
  for (const size of [32, 48, 128, 256]) {
    const page = await browser.newPage({ viewport: { width: size, height: size }, deviceScaleFactor: 1 });
    await page.setContent(`<style>html,body{margin:0;background:transparent}svg{width:100vw;height:100vh;display:block}</style>${svg}`);
    images.push({ size, png: await page.screenshot({ omitBackground: true }) }); await page.close();
  }
  const header = Buffer.alloc(6 + 16 * images.length); header.writeUInt16LE(1, 2); header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach(({ size, png }, i) => { const p = 6 + i * 16; header[p] = header[p + 1] = size === 256 ? 0 : size; header.writeUInt16LE(1, p + 4); header.writeUInt16LE(32, p + 6); header.writeUInt32LE(png.length, p + 8); header.writeUInt32LE(offset, p + 12); offset += png.length; });
  await mkdir('desktop/assets', { recursive: true }); await writeFile('desktop/assets/thermal.ico', Buffer.concat([header, ...images.map(i => i.png)]));
  console.log('Windows icon generated from the existing ThermalScope vector.');
} finally { await browser.close(); }

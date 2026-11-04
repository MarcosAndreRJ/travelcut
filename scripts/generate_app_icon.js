/**
 * Script para geração do ícone nativo multirresolução do TravelCut Studio (.ico e .png).
 * Suporta 16x16, 24x24, 32x32, 48x48, 64x64, 128x128 e 256x256.
 * Reutiliza a identidade visual existente: gradiente Ciano-Azul (#06B6D4 -> #2563EB)
 * e o emblema "TC" com ângulo de corte cinematográfico.
 */

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

// Tabela CRC32 padrão para PNG
const CRC_TABLE = new Uint32Array(256);
for (let i = 0; i < 256; i++) {
  let c = i;
  for (let k = 0; k < 8; k++) {
    c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
  }
  CRC_TABLE[i] = c;
}

function crc32(buf) {
  let c = 0xFFFFFFFF;
  for (let i = 0; i < buf.length; i++) {
    c = CRC_TABLE[(c ^ buf[i]) & 0xFF] ^ (c >>> 8);
  }
  return (c ^ 0xFFFFFFFF) >>> 0;
}

function pngChunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const typeBuf = Buffer.from(type, 'ascii');
  const crcBuf = Buffer.alloc(4);
  const crcVal = crc32(Buffer.concat([typeBuf, data]));
  crcBuf.writeUInt32BE(crcVal, 0);
  return Buffer.concat([len, typeBuf, data, crcBuf]);
}

function makePng(width, height, rgbaBuffer) {
  const signature = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

  // IHDR: width(4), height(4), bitDepth(1)=8, colorType(1)=6 (RGBA), compression(1)=0, filter(1)=0, interlace(1)=0
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;

  // Scanlines com byte de filtro 0 (None)
  const rowBytes = width * 4;
  const filtered = Buffer.alloc(height * (rowBytes + 1));
  for (let y = 0; y < height; y++) {
    filtered[y * (rowBytes + 1)] = 0; // Filter: None
    rgbaBuffer.copy(filtered, y * (rowBytes + 1) + 1, y * rowBytes, (y + 1) * rowBytes);
  }

  const idatData = zlib.deflateSync(filtered, { level: 9 });

  return Buffer.concat([
    signature,
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', idatData),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

function renderTravelCutIcon(size) {
  const buf = Buffer.alloc(size * size * 4);
  const radius = Math.max(2, Math.round(size * 0.22));

  function setPixel(x, y, r, g, b, a) {
    if (x < 0 || x >= size || y < 0 || y >= size) return;
    const idx = (y * size + x) * 4;
    const bgA = buf[idx + 3] / 255;
    const srcA = a / 255;
    const outA = srcA + bgA * (1 - srcA);
    if (outA <= 0) return;

    buf[idx + 0] = Math.round((r * srcA + buf[idx + 0] * bgA * (1 - srcA)) / outA);
    buf[idx + 1] = Math.round((g * srcA + buf[idx + 1] * bgA * (1 - srcA)) / outA);
    buf[idx + 2] = Math.round((b * srcA + buf[idx + 2] * bgA * (1 - srcA)) / outA);
    buf[idx + 3] = Math.round(outA * 255);
  }

  // 1. Fundo com cantos arredondados e gradiente ciano -> azul vibrante
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const qx = Math.max(0, Math.abs(x - (size - 1) / 2) - ((size - 1) / 2 - radius));
      const qy = Math.max(0, Math.abs(y - (size - 1) / 2) - ((size - 1) / 2 - radius));
      const dist = Math.sqrt(qx * qx + qy * qy);

      let alpha = 1.0;
      if (dist > radius) {
        alpha = Math.max(0, 1.0 - (dist - radius));
      }
      if (alpha <= 0) continue;

      // Gradiente diagonal (top-left #06b6d4 -> bottom-right #1d4ed8 / #0f172a)
      const t = (x + y) / (2 * (size - 1));
      let r, g, b;
      if (t < 0.6) {
        const u = t / 0.6;
        r = Math.round(6 + (37 - 6) * u);
        g = Math.round(182 + (99 - 182) * u);
        b = Math.round(212 + (235 - 212) * u);
      } else {
        const u = (t - 0.6) / 0.4;
        r = Math.round(37 + (15 - 37) * u);
        g = Math.round(99 + (23 - 99) * u);
        b = Math.round(235 + (60 - 235) * u);
      }

      // Sombra interna e borda ciano suave
      const edgeDist = Math.min(x, y, size - 1 - x, size - 1 - y);
      if (edgeDist === 0 && size >= 24) {
        // Borda sutil
        r = Math.min(255, r + 40);
        g = Math.min(255, g + 40);
        b = Math.min(255, b + 40);
      }

      setPixel(x, y, r, g, b, alpha * 255);
    }
  }

  // 2. Renderização do Glifo "TC"
  // Definido em grade normalizada [0..1]
  const scale = size;

  function drawRect(rx, ry, rw, rh, r, g, b, a) {
    const minX = Math.max(0, Math.floor(rx * scale));
    const maxX = Math.min(size - 1, Math.ceil((rx + rw) * scale));
    const minY = Math.max(0, Math.floor(ry * scale));
    const maxY = Math.min(size - 1, Math.ceil((ry + rh) * scale));

    for (let py = minY; py <= maxY; py++) {
      for (let px = minX; px <= maxX; px++) {
        // anti-aliasing de borda
        const fx = px / scale;
        const fy = py / scale;
        const inX = Math.max(0, Math.min(1, (fx + 1/scale - rx) * scale, (rx + rw - fx) * scale));
        const inY = Math.max(0, Math.min(1, (fy + 1/scale - ry) * scale, (ry + rh - fy) * scale));
        const coverage = Math.min(1, Math.max(0, inX * inY));
        if (coverage > 0) {
          setPixel(px, py, r, g, b, a * coverage * 255);
        }
      }
    }
  }

  // Sombra suave do glifo para alto contraste
  const shadowAlpha = 0.4;
  const shadowOffset = Math.max(1, Math.round(size * 0.02)) / scale;

  // Letra 'T': Barra superior + haste vertical
  // Posicionamento normalizado:
  // T: x=[0.18..0.52], y=[0.24..0.76]
  const tBarY = 0.26;
  const tBarH = Math.max(0.08, (size <= 24 ? 0.16 : 0.11));
  const tBarX = 0.18;
  const tBarW = 0.32;

  const tStemW = Math.max(0.08, (size <= 24 ? 0.15 : 0.11));
  const tStemX = tBarX + (tBarW - tStemW) / 2;
  const tStemY = tBarY + tBarH;
  const tStemH = 0.48 - tBarH;

  // Sombra T
  drawRect(tBarX + shadowOffset, tBarY + shadowOffset, tBarW, tBarH, 10, 20, 35, shadowAlpha);
  drawRect(tStemX + shadowOffset, tStemY + shadowOffset, tStemW, tStemH, 10, 20, 35, shadowAlpha);

  // Glifo T (Branco com leve tom ciano)
  drawRect(tBarX, tBarY, tBarW, tBarH, 245, 250, 255, 1.0);
  drawRect(tStemX, tStemY, tStemW, tStemH, 245, 250, 255, 1.0);

  // Letra 'C': Barra superior, haste vertical esquerda, barra inferior
  // C: x=[0.52..0.82], y=[0.26..0.74]
  const cX = 0.52;
  const cW = 0.30;
  const cBarH = tBarH;
  const cStemW = tStemW;
  const cStemY = tBarY;
  const cStemH = 0.48;
  const cBottomY = tBarY + cStemH - cBarH;

  // Sombra C
  drawRect(cX + shadowOffset, tBarY + shadowOffset, cW, cBarH, 10, 20, 35, shadowAlpha);
  drawRect(cX + shadowOffset, cStemY + shadowOffset, cStemW, cStemH, 10, 20, 35, shadowAlpha);
  drawRect(cX + shadowOffset, cBottomY + shadowOffset, cW, cBarH, 10, 20, 35, shadowAlpha);

  // Glifo C (Branco com leve tom ciano)
  drawRect(cX, tBarY, cW, cBarH, 245, 250, 255, 1.0);
  drawRect(cX, cStemY, cStemW, cStemH, 245, 250, 255, 1.0);
  drawRect(cX, cBottomY, cW, cBarH, 245, 250, 255, 1.0);

  // Corte diagonal estilizado (desbaste / corte de viagem) no C para tamanhos >= 32
  if (size >= 32) {
    const cutX = cX + cW - cBarH;
    const cutY = tBarY;
    // Ponto de corte ciano neon
    drawRect(cutX, cutY, cBarH, cBarH, 56, 189, 248, 0.9);
  }

  return buf;
}

function buildIco(images) {
  // images: array de { size: number, pngBuffer: Buffer }
  const count = images.length;
  const headerSize = 6 + count * 16;

  // Calcular offsets
  let offset = headerSize;
  const entries = [];
  for (const img of images) {
    entries.push({
      width: img.size === 256 ? 0 : img.size,
      height: img.size === 256 ? 0 : img.size,
      colorCount: 0,
      reserved: 0,
      planes: 1,
      bitCount: 32,
      bytesInRes: img.pngBuffer.length,
      imageOffset: offset,
      buffer: img.pngBuffer
    });
    offset += img.pngBuffer.length;
  }

  const icoBuffer = Buffer.alloc(offset);

  // ICONDIR Header (6 bytes)
  icoBuffer.writeUInt16LE(0, 0); // Reserved (0)
  icoBuffer.writeUInt16LE(1, 2); // Type: 1 = ICO
  icoBuffer.writeUInt16LE(count, 4); // Number of images

  // ICONDIRENTRY Entries (16 bytes per image)
  let entryPos = 6;
  for (const e of entries) {
    icoBuffer.writeUInt8(e.width, entryPos + 0);
    icoBuffer.writeUInt8(e.height, entryPos + 1);
    icoBuffer.writeUInt8(e.colorCount, entryPos + 2);
    icoBuffer.writeUInt8(e.reserved, entryPos + 3);
    icoBuffer.writeUInt16LE(e.planes, entryPos + 4);
    icoBuffer.writeUInt16LE(e.bitCount, entryPos + 6);
    icoBuffer.writeUInt32LE(e.bytesInRes, entryPos + 8);
    icoBuffer.writeUInt32LE(e.imageOffset, entryPos + 12);
    entryPos += 16;
  }

  // Escrever os buffers das imagens
  for (const e of entries) {
    e.buffer.copy(icoBuffer, e.imageOffset);
  }

  return icoBuffer;
}

function main() {
  const SIZES = [16, 24, 32, 48, 64, 128, 256];
  console.log('Gerando ícones do TravelCut Studio nas resoluções:', SIZES.join(', '));

  const images = [];
  for (const size of SIZES) {
    const rgba = renderTravelCutIcon(size);
    const png = makePng(size, size, rgba);
    images.push({ size, pngBuffer: png });
    console.log(`- Resolução ${size}x${size} gerada: ${png.length} bytes (PNG)`);
  }

  const ico = buildIco(images);
  console.log(`Arquivo .ico construído com sucesso: ${ico.length} bytes com ${images.length} resoluções.`);

  // Salvar em TravelCut.Desktop
  const desktopDir = path.resolve(__dirname, '../src/TravelCut.Desktop');
  const desktopIcoPath = path.join(desktopDir, 'app.ico');
  fs.writeFileSync(desktopIcoPath, ico);
  console.log(`Salvo: ${desktopIcoPath}`);

  // Salvar em frontend/public
  const frontendPublicDir = path.resolve(__dirname, '../frontend/public');
  if (!fs.existsSync(frontendPublicDir)) {
    fs.mkdirSync(frontendPublicDir, { recursive: true });
  }
  const faviconPath = path.join(frontendPublicDir, 'favicon.ico');
  fs.writeFileSync(faviconPath, ico);
  console.log(`Salvo: ${faviconPath}`);

  // Salvar também ícone de alta resolução PNG 256x256
  const iconPngPath = path.join(frontendPublicDir, 'icon-256.png');
  const img256 = images.find(img => img.size === 256);
  if (img256) {
    fs.writeFileSync(iconPngPath, img256.pngBuffer);
    console.log(`Salvo: ${iconPngPath}`);
  }
}

main();

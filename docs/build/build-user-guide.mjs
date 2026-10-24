// Builds docs/Minecraft-Server-Manager-User-Guide.docx from the wiki Markdown in docs/wiki.
// Usage: npm install && node build-user-guide.mjs
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { marked } from 'marked';
import {
  AlignmentType, BorderStyle, Document, ExternalHyperlink, Footer, Header, HeadingLevel, ImageRun, LevelFormat,
  Packer, PageNumber, Paragraph, ShadingType, Table, TableCell, TableOfContents, TableRow, TextRun, WidthType,
} from 'docx';

const here = path.dirname(fileURLToPath(import.meta.url));
const wiki = path.resolve(here, '../wiki');
const output = path.resolve(here, '../Minecraft-Server-Manager-User-Guide.docx');

const VERSION = '1.0.5';
const DATE = 'October 2026';

// Chapters in reading order. `title` overrides the page's own H1.
const CHAPTERS = [
  { file: path.join(here, 'introduction.md') },
  { file: 'Quick-Start.md' },
  { file: 'Installation.md' },
  { file: 'User-Guide.md', title: 'Getting Around the Panel' },
  { file: 'Dashboard.md' },
  { file: 'Console.md' },
  { file: 'Server-Properties.md' },
  { file: 'Worlds.md' },
  { file: 'Backups.md' },
  { file: 'Updates.md' },
  { file: 'Players.md' },
  { file: 'Settings.md' },
  { file: 'Running-as-a-Windows-Service.md' },
  { file: 'Remote-Access-and-HTTPS.md' },
  { file: 'Troubleshooting.md' },
  { file: 'FAQ.md', title: 'Frequently Asked Questions' },
  { file: 'Server-Properties-Reference.md', title: 'Appendix A: Server Properties Reference' },
  { file: 'Files-and-Folders.md', title: 'Appendix B: Files and Folders' },
];

// A4 with 2 cm margins.
const PAGE = { width: 11906, height: 16838, margin: 1134 };
const CONTENT_DXA = PAGE.width - 2 * PAGE.margin;
const CONTENT_PX = Math.floor((CONTENT_DXA / 1440) * 96);

const COLOR = {
  heading: '2E6B30', text: '1F2328', muted: '5A6670', border: 'D0D7DE',
  headerFill: 'E7F1E7', codeFill: 'F2F4F6', calloutFill: 'EEF6EE', calloutBorder: '3C8D3F', link: '1F6FB2',
};
const FONT = 'Calibri';
const MONO = 'Consolas';

let figureNumber = 0;
let listInstance = 0;

// ---------------------------------------------------------------------------------------------
// Inline content

const decode = (s) => s.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');

function inline(tokens = [], style = {}) {
  const runs = [];
  for (const t of tokens) {
    switch (t.type) {
      case 'text':
        if (t.tokens?.length) runs.push(...inline(t.tokens, style));
        else runs.push(new TextRun({ text: decode(t.text), ...style }));
        break;
      case 'escape':
        runs.push(new TextRun({ text: decode(t.text), ...style }));
        break;
      case 'strong':
        runs.push(...inline(t.tokens, { ...style, bold: true }));
        break;
      case 'em':
        runs.push(...inline(t.tokens, { ...style, italics: true }));
        break;
      case 'del':
        runs.push(...inline(t.tokens, { ...style, strike: true }));
        break;
      case 'codespan':
        runs.push(new TextRun({
          text: decode(t.text), ...style, font: MONO, size: (style.size ?? 21) - 2,
          shading: { type: ShadingType.CLEAR, fill: COLOR.codeFill, color: 'auto' },
        }));
        break;
      case 'link':
        if (/^https?:/.test(t.href)) {
          runs.push(new ExternalHyperlink({
            link: t.href,
            children: inline(t.tokens, { ...style, color: COLOR.link, underline: {} }),
          }));
        } else {
          runs.push(...inline(t.tokens, style)); // wiki page links become plain text
        }
        break;
      case 'image':
        runs.push(image(t.href, t.text, { maxWidth: style.imageMaxWidth ?? CONTENT_PX }));
        break;
      case 'br':
        runs.push(new TextRun({ break: 1 }));
        break;
      default:
        if (t.text) runs.push(new TextRun({ text: decode(t.text), ...style }));
    }
  }
  return runs;
}

// ---------------------------------------------------------------------------------------------
// Images

function pngSize(buffer) {
  return { width: buffer.readUInt32BE(16), height: buffer.readUInt32BE(20) };
}

function image(href, alt, { maxWidth = CONTENT_PX, maxHeight = 820 } = {}) {
  const file = path.resolve(wiki, href);
  const data = fs.readFileSync(file);
  const { width, height } = pngSize(data);
  let w = Math.min(maxWidth, width);
  let h = Math.round((w * height) / width);
  if (h > maxHeight) { h = maxHeight; w = Math.round((h * width) / height); }
  return new ImageRun({
    type: 'png', data, transformation: { width: w, height: h },
    altText: { title: alt, description: alt, name: path.basename(file) },
  });
}

function figure(token) {
  figureNumber++;
  const isPhone = /mobile/.test(token.href);
  return [
    new Paragraph({
      alignment: AlignmentType.CENTER, keepNext: true, spacing: { before: 120, after: 60 },
      children: [image(token.href, token.text, isPhone ? { maxHeight: 520 } : {})],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER, spacing: { after: 200 },
      children: [new TextRun({ text: `Figure ${figureNumber}: ${token.text}`, italics: true, size: 18, color: COLOR.muted })],
    }),
  ];
}

// ---------------------------------------------------------------------------------------------
// Blocks

function blocks(tokens, ctx = {}) {
  const out = [];
  for (const t of tokens) {
    switch (t.type) {
      case 'heading': {
        const level = [HeadingLevel.HEADING_1, HeadingLevel.HEADING_2, HeadingLevel.HEADING_3, HeadingLevel.HEADING_4][Math.min(t.depth, 4) - 1];
        const children = t.depth === 1 && ctx.title ? [new TextRun(ctx.title)] : inline(t.tokens);
        out.push(new Paragraph({ heading: level, pageBreakBefore: t.depth === 1, keepNext: true, children }));
        break;
      }
      case 'paragraph': {
        const images = t.tokens.filter((x) => x.type === 'image');
        const onlyImages = images.length > 0 && t.tokens.every((x) => x.type === 'image' || (x.type === 'text' && !x.text.trim()));
        if (onlyImages) images.forEach((img) => out.push(...figure(img)));
        else out.push(paragraph(inline(t.tokens), ctx));
        break;
      }
      case 'text':
        out.push(paragraph(inline(t.tokens ?? [{ type: 'text', text: t.text }]), ctx));
        break;
      case 'list':
        out.push(...list(t, ctx.level ?? 0, ctx));
        break;
      case 'table':
        out.push(table(t), new Paragraph({ spacing: { after: 80 }, children: [] }));
        break;
      case 'blockquote':
        out.push(...blocks(t.tokens, { ...ctx, callout: true }));
        break;
      case 'code':
        if (t.lang !== 'mermaid') out.push(...code(t.text, ctx));
        break;
      case 'hr':
        out.push(new Paragraph({ border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: COLOR.border, space: 1 } }, children: [] }));
        break;
      default:
        break; // 'space', 'html', etc.
    }
  }
  return out;
}

function paragraph(children, ctx) {
  const options = { children, spacing: { after: 120 } };
  if (ctx.numbering) options.numbering = ctx.numbering;
  if (ctx.indent) options.indent = { left: ctx.indent };
  if (ctx.callout) {
    options.shading = { type: ShadingType.CLEAR, fill: COLOR.calloutFill, color: 'auto' };
    options.border = { left: { style: BorderStyle.SINGLE, size: 24, color: COLOR.calloutBorder, space: 8 } };
    options.indent = { left: (ctx.indent ?? 0) + 160, right: 160 };
    options.spacing = { before: 60, after: 160 };
  }
  return new Paragraph(options);
}

function code(text, ctx) {
  const lines = text.split('\n');
  return lines.map((line, i) => new Paragraph({
    indent: { left: (ctx.indent ?? 0) + 160 },
    shading: { type: ShadingType.CLEAR, fill: COLOR.codeFill, color: 'auto' },
    spacing: { before: i === 0 ? 60 : 0, after: i === lines.length - 1 ? 160 : 0, line: 240 },
    keepLines: true, keepNext: i < lines.length - 1,
    children: [new TextRun({ text: line.length ? line : ' ', font: MONO, size: 17 })],
  }));
}

function list(token, level, ctx) {
  const reference = token.ordered ? 'numbers' : 'bullets';
  const instance = ++listInstance;
  const out = [];
  for (const item of token.items) {
    let first = true;
    for (const child of item.tokens) {
      if (child.type === 'list') {
        out.push(...list(child, level + 1, ctx));
        continue;
      }
      const itemCtx = first
        ? { ...ctx, numbering: { reference, level, instance }, indent: undefined }
        : { ...ctx, numbering: undefined, indent: 720 * (level + 1) };
      out.push(...blocks([child], itemCtx));
      if (child.type !== 'space') first = false;
    }
  }
  return out;
}

function table(token) {
  const columns = token.header.length;
  const plain = (cell) => cell.text.replace(/!\[[^\]]*\]\([^)]*\)/g, 'x'.repeat(30));
  // Weight columns by their longest text, but never narrower than their longest unbreakable word.
  const weights = token.header.map((h, c) => {
    const texts = [plain(h), ...token.rows.map((r) => plain(r[c]))];
    const longest = Math.max(...texts.map((s) => s.length));
    const longestWord = Math.max(...texts.flatMap((s) => s.split(/\s+/)).map((w) => w.length));
    return Math.max(Math.min(60, Math.max(8, longest)), longestWord * 1.3);
  });
  const total = weights.reduce((a, b) => a + b, 0);
  const widths = weights.map((w) => Math.floor((CONTENT_DXA * w) / total));
  widths[widths.length - 1] += CONTENT_DXA - widths.reduce((a, b) => a + b, 0);

  const border = { style: BorderStyle.SINGLE, size: 4, color: COLOR.border };
  const borders = { top: border, bottom: border, left: border, right: border };
  const cell = (c, i, header) => new TableCell({
    width: { size: widths[i], type: WidthType.DXA },
    borders,
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    shading: header ? { type: ShadingType.CLEAR, fill: COLOR.headerFill, color: 'auto' } : undefined,
    children: [new Paragraph({
      spacing: { after: 0 },
      keepNext: header, // keep the header row on the same page as the first data row
      children: inline(c.tokens, {
        size: 19, bold: header || undefined,
        imageMaxWidth: Math.floor((widths[i] / 1440) * 96) - 16,
      }),
    })],
  });

  const rows = [new TableRow({ tableHeader: true, cantSplit: true, children: token.header.map((c, i) => cell(c, i, true)) })];
  for (const r of token.rows) rows.push(new TableRow({ cantSplit: true, children: r.map((c, i) => cell(c, i, false)) }));
  return new Table({ width: { size: CONTENT_DXA, type: WidthType.DXA }, columnWidths: widths, rows, alignment: AlignmentType.CENTER });
}

// ---------------------------------------------------------------------------------------------
// Document

function chapter({ file, title }) {
  const fullPath = path.isAbsolute(file) ? file : path.join(wiki, file);
  const tokens = marked.lexer(fs.readFileSync(fullPath, 'utf8'));
  return blocks(tokens, { title });
}

function cover() {
  const logo = fs.readFileSync(path.join(here, 'logo.png'));
  const shot = fs.readFileSync(path.join(wiki, 'images/dashboard.png'));
  const { width, height } = pngSize(shot);
  const shotWidth = 560;
  return [
    new Paragraph({ spacing: { before: 1400, after: 300 }, children: [new ImageRun({ type: 'png', data: logo, transformation: { width: 96, height: 96 }, altText: { title: 'Logo', description: 'Grass block logo', name: 'logo' } })] }),
    new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: 'Minecraft Server Manager', font: FONT, size: 64, bold: true, color: COLOR.text })] }),
    new Paragraph({ spacing: { after: 360 }, children: [new TextRun({ text: 'User Guide', font: FONT, size: 40, color: COLOR.heading })] }),
    new Paragraph({
      spacing: { after: 600 },
      border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: COLOR.calloutBorder, space: 8 } },
      children: [new TextRun({ text: `Version ${VERSION}  ·  ${DATE}`, size: 24, color: COLOR.muted })],
    }),
    new Paragraph({ children: [new ImageRun({ type: 'png', data: shot, transformation: { width: shotWidth, height: Math.round((shotWidth * height) / width) }, altText: { title: 'Dashboard', description: 'The panel dashboard', name: 'cover' } })] }),
    new Paragraph({ spacing: { before: 600 }, children: [new TextRun({ text: 'Web control panel for Minecraft: Java Edition dedicated servers on Windows Server.', size: 22, color: COLOR.muted })] }),
    new Paragraph({ spacing: { before: 120 }, children: [new TextRun({ text: 'Minecraft is a trademark of Mojang AB. This project is not affiliated with Mojang or Microsoft.', size: 16, color: COLOR.muted, italics: true })] }),
  ];
}

const doc = new Document({
  creator: 'Minecraft Server Manager',
  title: 'Minecraft Server Manager – User Guide',
  description: `User guide for Minecraft Server Manager ${VERSION}`,
  features: { updateFields: true },
  styles: {
    default: {
      document: { run: { font: FONT, size: 21, color: COLOR.text }, paragraph: { spacing: { after: 120, line: 276 } } },
    },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 40, bold: true, color: COLOR.heading },
        paragraph: { spacing: { before: 0, after: 240 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 28, bold: true, color: COLOR.heading },
        paragraph: { spacing: { before: 320, after: 120 }, outlineLevel: 1 } },
      { id: 'Heading3', name: 'Heading 3', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 23, bold: true, color: COLOR.text },
        paragraph: { spacing: { before: 240, after: 80 }, outlineLevel: 2 } },
      { id: 'Heading4', name: 'Heading 4', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { font: FONT, size: 21, bold: true, italics: true, color: COLOR.text },
        paragraph: { spacing: { before: 200, after: 60 }, outlineLevel: 3 } },
    ],
  },
  numbering: {
    config: [
      { reference: 'bullets', levels: [
        { level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
        { level: 1, format: LevelFormat.BULLET, text: '◦', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1440, hanging: 360 } } } },
      ] },
      { reference: 'numbers', levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } },
        { level: 1, format: LevelFormat.LOWER_LETTER, text: '%2.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 1440, hanging: 360 } } } },
      ] },
    ],
  },
  sections: [
    {
      properties: { page: { size: { width: PAGE.width, height: PAGE.height }, margin: { top: PAGE.margin, bottom: PAGE.margin, left: PAGE.margin, right: PAGE.margin } } },
      children: cover(),
    },
    {
      properties: {
        page: {
          size: { width: PAGE.width, height: PAGE.height },
          margin: { top: PAGE.margin, bottom: PAGE.margin, left: PAGE.margin, right: PAGE.margin, header: 567, footer: 567 },
          pageNumbers: { start: 1 },
        },
      },
      headers: {
        default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new TextRun({ text: 'Minecraft Server Manager · User Guide', size: 16, color: COLOR.muted })] })] }),
      },
      footers: {
        default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ children: ['Page ', PageNumber.CURRENT, ' of ', PageNumber.TOTAL_PAGES], size: 16, color: COLOR.muted })] })] }),
      },
      children: [
        new Paragraph({ children: [new TextRun({ text: 'Contents', font: FONT, size: 40, bold: true, color: COLOR.heading })], spacing: { after: 240 } }),
        new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }),
        ...CHAPTERS.flatMap(chapter),
      ],
    },
  ],
});

fs.writeFileSync(output, await Packer.toBuffer(doc));
console.log(`Wrote ${path.relative(process.cwd(), output)} (${figureNumber} figures, ${(fs.statSync(output).size / 1048576).toFixed(1)} MB)`);

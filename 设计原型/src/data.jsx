// Seed data for DesktopFences prototype. Exposed as globals.
const ICON_COLORS = {
  doc:  ['#2b5cae', '#4b7fd4'],
  xls:  ['#1e7d4a', '#3aa76b'],
  ppt:  ['#c43e1c', '#e0653f'],
  pdf:  ['#c02535', '#e04a5a'],
  img:  ['#7b4fb5', '#9a6ed0'],
  code: ['#3a6f8e', '#5a99bb'],
  sql:  ['#2f7d7d', '#4aa8a8'],
  ps1:  ['#1e3a7a', '#3455a8'],
  txt:  ['#4a5264', '#6a7386'],
  md:   ['#2f4858', '#4a6878'],
  exe:  ['#4d4d4d', '#707070'],
  folder:['#c9a23f', '#e5bf5a'],
  zip:  ['#6b5b3e', '#8d7a58'],
  video:['#a02e5a', '#c85581'],
};
function iconFor(name) {
  const ext = (name.split('.').pop() || '').toLowerCase();
  const folder = !name.includes('.');
  if (folder) return { kind: 'folder', label: 'DIR' };
  if (['doc','docx','rtf'].includes(ext)) return { kind: 'doc', label: 'W' };
  if (['xls','xlsx','csv'].includes(ext)) return { kind: 'xls', label: 'X' };
  if (['ppt','pptx'].includes(ext)) return { kind: 'ppt', label: 'P' };
  if (['pdf'].includes(ext)) return { kind: 'pdf', label: 'PDF' };
  if (['png','jpg','jpeg','gif','webp','bmp','svg','ico'].includes(ext)) return { kind: 'img', label: 'IMG' };
  if (['js','ts','jsx','tsx','py','cs','cpp','go','rs','java','html','css'].includes(ext)) return { kind: 'code', label: '</>' };
  if (['sql','db'].includes(ext)) return { kind: 'sql', label: 'SQL' };
  if (['ps1','bat','cmd','sh'].includes(ext)) return { kind: 'ps1', label: '>_' };
  if (['txt','log'].includes(ext)) return { kind: 'txt', label: 'TXT' };
  if (['md','markdown'].includes(ext)) return { kind: 'md', label: 'MD' };
  if (['exe','msi','lnk','url'].includes(ext)) return { kind: 'exe', label: 'EXE' };
  if (['zip','rar','7z','tar','gz'].includes(ext)) return { kind: 'zip', label: 'ZIP' };
  if (['mp4','mkv','avi','mov','webm'].includes(ext)) return { kind: 'video', label: 'MP4' };
  return { kind: 'txt', label: ext.toUpperCase().slice(0,3) || '?' };
}

const INITIAL_FENCES = [
  {
    id: 'f1',
    title: '文件夹',
    x: 110, y: 30, w: 520, h: 180,
    rolled: false,
    path: 'C:\\Users\\Noraink\\Desktop',
    tabs: [
      { id: 't1a', title: '文件夹', files: [
        '项目方案V2','客户资料','周报存档','设计稿','备份_2026','会议纪要','OpenClaude','原型图'
      ]},
      { id: 't1b', title: '文档', files: [
        '需求文档.docx','测试报告.xlsx','架构设计.pptx','API说明.pdf','更新日志.md','README.txt','customer_data.csv','合同.docx','客户画像.xlsx','产品手册.pdf'
      ]},
      { id: 't1c', title: '图片', files: [
        'banner.png','logo.svg','screenshot.jpg','architecture.png','demo.gif','mockup.jpg','hero.webp','icon_256.ico'
      ]},
      { id: 't1d', title: 'SQL', files: [
        'schema.sql','migration_001.sql','backup_2026.db','customer.sql','queries.sql'
      ]},
      { id: 't1e', title: 'G:\\05point', files: [
        'start_claude.ps1','start_art.log','claude_resume.pdf','unpacked-App.ps1','launcher.bat'
      ]},
    ],
    activeTab: 0,
  },
  {
    id: 'f2',
    title: '程序及快捷方式',
    x: 110, y: 236, w: 320, h: 190,
    rolled: false,
    tabs: [{ id: 't2a', title: '程序及快捷方式', files: [
      'Visual Studio.lnk','Chrome.lnk','DBeaver.lnk','Figma.lnk','Terminal.lnk','Claude.lnk','Edge.lnk','Notion.lnk','Slack.lnk','PowerShell.lnk'
    ]}],
    activeTab: 0,
  },
  {
    id: 'f3',
    title: '代码仓库',
    x: 448, y: 236, w: 360, h: 190,
    rolled: false,
    path: 'C:\\Users\\Noraink\\Workspace',
    tabs: [{ id: 't3a', title: '代码仓库', files: [
      'DesktopFences','api-gateway','data-pipeline','ml-service','docs-site','build.ps1','deploy.sh','.editorconfig'
    ]}],
    activeTab: 0,
  },
  {
    id: 'f4',
    title: '图片',
    x: 824, y: 30, w: 280, h: 180,
    rolled: false,
    tabs: [{ id: 't4a', title: '图片', files: [
      '截图_001.png','截图_002.png','架构图.png','线框图.jpg','封面.webp','logo.svg','favicon.ico','屏幕截图.png','头像.jpg'
    ]}],
    activeTab: 0,
  },
  {
    id: 'f5',
    title: '最近文档',
    x: 824, y: 236, w: 280, h: 190,
    rolled: true,
    tabs: [{ id: 't5a', title: '最近文档', files: [
      '周会纪要.docx','预算表.xlsx','汇报PPT.pptx','合同.pdf','todo.md'
    ]}],
    activeTab: 0,
  },
];

// Desktop left-column icons (the ones not organized yet / free-standing)
const NATIVE_ICONS = [
  { name: '此电脑', kind: 'exe', label: 'PC' },
  { name: '回收站', kind: 'img', label: '♻' },
  { name: 'Edge', kind: 'code', label: 'E' },
  { name: 'VSCode', kind: 'ps1', label: '</>' },
  { name: 'WeChat', kind: 'xls', label: 'W' },
  { name: 'Claude', kind: 'doc', label: 'C' },
  { name: '未分类报告.docx', kind: 'doc', label: 'W' },
];

window.ICON_COLORS = ICON_COLORS;
window.iconFor = iconFor;
window.INITIAL_FENCES = INITIAL_FENCES;
window.NATIVE_ICONS = NATIVE_ICONS;

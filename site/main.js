// HomeWindow website: language, theme, screenshots, the latest download and a live taskbar
(function () {
  'use strict';
  var root = document.documentElement;
  root.classList.add('js');
  var REPO = 'WNijhof/homewindow';

  function store(key, value) { try { localStorage.setItem(key, value); } catch (e) {} }
  function lang() { return root.lang === 'nl' ? 'nl' : 'en'; }
  var darkQuery = window.matchMedia('(prefers-color-scheme: dark)');
  function isDark() { return root.dataset.theme === 'dark' || (root.dataset.theme !== 'light' && darkQuery.matches); }

  // ---- Screenshots in the language and theme of the page ----
  function shotSrc(name) { return 'img/' + name + '-' + lang() + '-' + (isDark() ? 'dark' : 'light') + '.webp'; }
  function updateShots() {
    document.querySelectorAll('img[data-shot]').forEach(function (img) {
      var src = shotSrc(img.dataset.shot);
      if (img.getAttribute('src') !== src) img.setAttribute('src', src);
    });
  }

  // ---- Theme: automatic, light, dark ----
  var themeButton = document.getElementById('theme');
  var themeIcons = { auto: '#i-auto', light: '#i-sunsmall', dark: '#i-moon' };
  var themeNames = { auto: ['Automatisch', 'Automatic'], light: ['Licht', 'Light'], dark: ['Donker', 'Dark'] };
  function showTheme() {
    var t = root.dataset.theme || 'auto';
    themeButton.querySelector('use').setAttribute('href', themeIcons[t]);
    var name = themeNames[t][lang() === 'nl' ? 0 : 1];
    themeButton.title = (lang() === 'nl' ? 'Thema: ' : 'Theme: ') + name;
    updateShots();
  }
  themeButton.addEventListener('click', function () {
    var next = { auto: 'light', light: 'dark', dark: 'auto' }[root.dataset.theme || 'auto'];
    root.dataset.theme = next;
    store('hw-theme', next);
    showTheme();
  });
  darkQuery.addEventListener('change', updateShots);

  // ---- Language ----
  document.getElementById('lang').addEventListener('click', function () {
    root.lang = lang() === 'nl' ? 'en' : 'nl';
    store('hw-lang', root.lang);
    showTheme();
    showTab(currentTab, false);
    showDownload();
  });

  // ---- The main window, page by page ----
  var captions = {
    overview: ['Het weer, het verbruik van nu, wie er thuis is en je favorieten in één oogopslag.', 'The weather, power right now, who is home and your favourites at a glance.'],
    devices: ['Alle apparaten per kamer, met zoeken, filteren op soort en kamers die je inklapt.', 'Every device per room, with search, a filter per type and rooms you can collapse.'],
    energy: ['Nu, vandaag en deze maand, met zon, net, gas en je thuisbatterij.', 'Now, today and this month, with solar, grid, gas and your home battery.'],
    flows: ['Flows per map, ook geavanceerde flows. Eén klik start ze.', 'Flows per folder, advanced flows too. One click starts them.'],
    batteries: ['Alle apparaten op batterijen, de leegste bovenaan, met het type batterij.', 'All battery-powered devices, the emptiest first, with the battery type.'],
    system: ['De gezondheid van je Homey: geheugen, opslag, updates en apps die vastlopen.', 'The health of your Homey: memory, storage, updates and apps that crashed.'],
  };
  var showcase = document.getElementById('showcase');
  var caption = document.getElementById('caption');
  var tabs = document.querySelectorAll('.tab');
  var currentTab = 'overview';
  function showTab(name, fade) {
    currentTab = name;
    tabs.forEach(function (t) { t.setAttribute('aria-selected', String(t.dataset.tab === name)); });
    caption.textContent = captions[name][lang() === 'nl' ? 0 : 1];
    if (showcase.dataset.shot === name) return;
    if (!fade) { showcase.dataset.shot = name; updateShots(); return; }
    showcase.classList.add('fading');
    setTimeout(function () {
      showcase.dataset.shot = name;
      updateShots();
      var done = function () { showcase.classList.remove('fading'); };
      if (showcase.complete) done(); else { showcase.onload = done; setTimeout(done, 600); }
    }, 200);
  }
  tabs.forEach(function (t) { t.addEventListener('click', function () { showTab(t.dataset.tab, true); }); });
  // Load the other pages in the background, so switching is instant
  window.addEventListener('load', function () {
    setTimeout(function () { Object.keys(captions).forEach(function (n) { new Image().src = shotSrc(n); }); }, 1500);
  });

  // ---- The latest installer straight from GitHub ----
  var release = null;
  function showDownload() {
    if (!release) return;
    var nl = lang() === 'nl';
    document.querySelectorAll('.download-meta').forEach(function (el) {
      el.textContent = 'v' + release.version + ' · ' + release.size + ' MB · ' + (nl ? 'gratis' : 'free');
    });
  }
  fetch('https://api.github.com/repos/' + REPO + '/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (json) {
      if (!json) return;
      var asset = (json.assets || []).filter(function (a) { return /^HomeWindow-Setup-.*\.exe$/.test(a.name); })[0];
      if (!asset) return;
      release = { version: (json.tag_name || '').replace(/^v/i, ''), size: Math.round(asset.size / 1048576), url: asset.browser_download_url };
      document.querySelectorAll('.download-link').forEach(function (a) { a.href = release.url; });
      showDownload();
    })
    .catch(function () { /* the links keep pointing at the releases page */ });

  // ---- A living taskbar ----
  var live = { home: 1411, solar: 3667, battery: 62 };
  function fmt(n) { return Math.round(n) + ' W'; }
  function tick() {
    live.home = Math.max(380, live.home + (Math.random() - 0.5) * 140);
    live.solar = Math.max(0, live.solar + (Math.random() - 0.5) * 110);
    var batteryW = 600;
    var grid = live.home - live.solar + batteryW;
    live.battery = Math.min(100, live.battery + 0.04);
    var set = function (k, v) { var el = document.querySelector('[data-live="' + k + '"]'); if (el) el.textContent = v; };
    set('home', fmt(live.home));
    set('solar', fmt(live.solar));
    set('grid', fmt(grid));
    set('battery', Math.floor(live.battery) + ' %');
    var g = document.querySelector('[data-live="grid"]');
    if (g) g.classList.toggle('green', grid < 0);
    var now = new Date();
    var t = document.querySelector('.clock-time'), d = document.querySelector('.clock-date');
    if (t) t.textContent = now.toLocaleTimeString('nl-NL', { hour: '2-digit', minute: '2-digit' });
    if (d) d.textContent = now.toLocaleDateString(lang() === 'nl' ? 'nl-NL' : 'en-GB');
  }
  if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) setInterval(tick, 2500);
  tick();

  // ---- Appear on scroll, and a line under the bar once scrolled ----
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } });
    }, { rootMargin: '0px 0px -8% 0px' });
    document.querySelectorAll('.reveal').forEach(function (el, i) {
      el.style.transitionDelay = (el.closest('.features, .steps') ? (i % 4) * 70 : 0) + 'ms';
      io.observe(el);
    });
  } else {
    document.querySelectorAll('.reveal').forEach(function (el) { el.classList.add('in'); });
  }
  var nav = document.querySelector('.nav');
  window.addEventListener('scroll', function () { nav.classList.toggle('scrolled', window.scrollY > 8); }, { passive: true });

  showTheme();
  showTab('overview', false);
})();

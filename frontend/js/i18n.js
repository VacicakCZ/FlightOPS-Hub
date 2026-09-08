// Fetches locales/languages.json and locales/<lang>.json. Holds no reactive
// state itself - the Alpine store (state.js) owns both "current strings"
// and "languages" so that x-text/x-for bindings driven by $store.app
// actually re-render once these async loads resolve (Alpine only tracks
// reads through its own reactive proxies, not plain module-level
// variables - a plain-array version of this exact "languages" list once
// caused the Settings language <select> to permanently render zero
// <option>s, since its x-for ran once before the fetch had resolved).
window.I18n = (() => {
  let languagesCache = null;
  const stringsCache = {};

  async function loadLanguages() {
    if (languagesCache) return languagesCache;
    const res = await fetch("locales/languages.json");
    languagesCache = await res.json();
    return languagesCache;
  }

  async function loadStrings(lang) {
    if (stringsCache[lang]) return stringsCache[lang];
    const res = await fetch(`locales/${lang.toLowerCase()}.json`);
    stringsCache[lang] = await res.json();
    return stringsCache[lang];
  }

  return { loadLanguages, loadStrings };
})();

// Fetches locales/languages.json and locales/<lang>.json. Holds no reactive
// state itself - the Alpine store (state.js) owns the "current strings" so
// that x-text bindings using $store.app.t(...) actually re-render on
// language change (Alpine only tracks reads through its own reactive
// proxies, not plain module-level variables).
window.I18n = (() => {
  let languages = [];
  const cache = {};

  async function loadLanguages() {
    if (languages.length) return languages;
    const res = await fetch("locales/languages.json");
    languages = await res.json();
    return languages;
  }

  async function loadStrings(lang) {
    if (cache[lang]) return cache[lang];
    const res = await fetch(`locales/${lang.toLowerCase()}.json`);
    cache[lang] = await res.json();
    return cache[lang];
  }

  return { loadLanguages, loadStrings, getLanguages: () => languages };
})();

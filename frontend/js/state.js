// Global Alpine store: persisted config mirror + ephemeral UI state
// (active tab) + the current language's translation strings. Translation
// strings live here (not in a plain JS module) so that x-text="$store.app.t(...)"
// bindings are tracked by Alpine's reactivity and re-render on language change.
document.addEventListener("alpine:init", () => {
  Alpine.store("app", {
    activeTab: "flight",
    theme: "system",
    language: "EN",
    config: {},
    strings: {},

    async init() {
      this.config = await Api.configGet();
      this.theme = this.config._theme || "system";
      this.applyTheme(this.theme);

      const languages = await I18n.loadLanguages();
      const requestedLang = this.config._language || "EN";
      const lang = languages.some((l) => l.code === requestedLang) ? requestedLang : "EN";
      await this.setLanguage(lang, /* persist */ false);
    },

    t(key, ...args) {
      let template = this.strings[key] ?? key;
      args.forEach((arg, i) => {
        template = template.replaceAll(`{${i}}`, arg);
      });
      let idx = 0;
      template = template.replaceAll("{}", () => (args[idx] !== undefined ? args[idx++] : "{}"));
      return template;
    },

    // Splits "Main text (parenthetical)" into its two parts, so tab labels
    // can render the parenthetical on its own (smaller) second line instead
    // of being squeezed/truncated on one line.
    splitParen(text) {
      const match = text.trim().match(/^(.*?)\s*(\(.*\))\s*$/);
      return match ? { main: match[1], paren: match[2] } : { main: text.trim(), paren: "" };
    },

    tMain(key, ...args) {
      return this.splitParen(this.t(key, ...args)).main;
    },

    tParen(key, ...args) {
      return this.splitParen(this.t(key, ...args)).paren;
    },

    setTab(tab) {
      this.activeTab = tab;
    },

    applyTheme(theme) {
      const root = document.documentElement;
      if (theme === "dark" || theme === "light") {
        root.setAttribute("data-theme", theme);
      } else {
        root.removeAttribute("data-theme");
      }
    },

    async setTheme(theme) {
      this.theme = theme;
      this.applyTheme(theme);
      this.config = await Api.configSet({ _theme: theme });
    },

    async setLanguage(lang, persist = true) {
      this.strings = await I18n.loadStrings(lang);
      this.language = lang;
      if (persist) {
        this.config = await Api.configSet({ _language: lang });
      }
    },
  });
});

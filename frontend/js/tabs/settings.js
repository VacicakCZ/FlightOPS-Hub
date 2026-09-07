document.addEventListener("alpine:init", () => {
  Alpine.data("settingsTab", () => ({
    get languages() {
      return I18n.getLanguages();
    },
  }));
});

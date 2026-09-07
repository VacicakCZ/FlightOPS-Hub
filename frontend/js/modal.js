// Generic HTML modals replacing tkinter.messagebox / CTkInputDialog. Backed
// by a tiny Alpine component (#modal-root in index.html) driven through this
// module's promise-returning functions so callers can just `await`.
window.Modal = (() => {
  let resolver = null;

  function state() {
    return Alpine.store("modal");
  }

  function promptText(title, message, defaultValue = "") {
    return new Promise((resolve) => {
      resolver = resolve;
      state().open("prompt", title, message, defaultValue);
    });
  }

  function confirmDialog(title, message) {
    return new Promise((resolve) => {
      resolver = resolve;
      state().open("confirm", title, message, "");
    });
  }

  function alertMsg(title, message, variant = "warning") {
    return new Promise((resolve) => {
      resolver = resolve;
      state().open("alert", title, message, "", variant);
    });
  }

  function _resolve(value) {
    state().close();
    if (resolver) {
      resolver(value);
      resolver = null;
    }
  }

  return { promptText, confirmDialog, alertMsg, _resolve };
})();

document.addEventListener("alpine:init", () => {
  Alpine.store("modal", {
    visible: false,
    kind: "alert",
    title: "",
    message: "",
    value: "",
    variant: "warning",
    open(kind, title, message, value, variant = "warning") {
      this.kind = kind;
      this.title = title;
      this.message = message;
      this.value = value;
      this.variant = variant;
      this.visible = true;
    },
    close() {
      this.visible = false;
    },
    confirm() {
      window.Modal._resolve(this.kind === "prompt" ? this.value : true);
    },
    cancel() {
      window.Modal._resolve(this.kind === "prompt" ? null : false);
    },
  });
});

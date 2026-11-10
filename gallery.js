(() => {
  const storageKey = "playhub-site-theme";
  const preference = window.matchMedia("(prefers-color-scheme: dark)");
  const root = document.documentElement;
  let saved;
  try { saved = localStorage.getItem(storageKey); } catch { /* A blocked store still allows switching themes. */ }
  if (saved !== "dark" && saved !== "light") saved = undefined;

  function applyTheme(theme) {
    root.dataset.theme = theme;
    const toggle = document.querySelector(".theme-toggle");
    if (toggle) toggle.setAttribute("aria-pressed", String(theme === "dark"));
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "dark" ? "#171612" : "#fff9ec");
  }

  applyTheme(saved ?? (preference.matches ? "dark" : "light"));
  document.addEventListener("DOMContentLoaded", () => {
    const toggle = document.querySelector(".theme-toggle");
    toggle.hidden = false;
    applyTheme(root.dataset.theme);
    toggle.addEventListener("click", () => {
      saved = root.dataset.theme === "dark" ? "light" : "dark";
      try { localStorage.setItem(storageKey, saved); } catch { /* The current page keeps the choice. */ }
      applyTheme(saved);
    });
    preference.addEventListener("change", event => {
      if (!saved) applyTheme(event.matches ? "dark" : "light");
    });

    for (const gallery of document.querySelectorAll("[data-carousel]")) {
      const slides = Array.from(gallery.querySelectorAll("[data-slide]"));
      const controls = gallery.querySelector(".media-controls");
      const previous = controls?.querySelector("[data-previous]");
      const next = controls?.querySelector("[data-next]");
      if (slides.length < 2 || !previous || !next) continue;

      let current = 0;
      const status = document.createElement("span");
      status.setAttribute("role", "status");
      status.style.cssText = "position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%)";
      controls.append(status);
      const show = index => {
        slides[current].hidden = true;
        current = (index + slides.length) % slides.length;
        slides[current].hidden = false;
        status.textContent = slides[current].querySelector("figcaption")?.textContent
          ?? slides[current].querySelector("img")?.alt ?? "";
      };
      controls.hidden = false;
      previous.addEventListener("click", () => show(current - 1));
      next.addEventListener("click", () => show(current + 1));
      gallery.addEventListener("keydown", event => {
        if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
        event.preventDefault();
        show(current + (event.key === "ArrowLeft" ? -1 : 1));
      });
    }
  });
})();

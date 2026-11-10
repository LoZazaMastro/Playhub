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

  function initSlideshow(gallery) {
    const slides = Array.from(gallery.querySelectorAll("[data-slide]"));
    const stage = gallery.querySelector("[data-slideshow-stage]");
    const previous = gallery.querySelector("[data-action-previous]");
    const next = gallery.querySelector("[data-action-next]");
    const playback = gallery.querySelector("[data-slideshow-playback]");
    const selectors = gallery.querySelector(".slide-selectors");
    const status = gallery.querySelector(".slideshow-status");
    const motionPreference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const interval = 5500;
    let current = 0;
    let playing = !motionPreference.matches;
    let inView = false;
    let hovering = false;
    let timer;

    const dots = selectors ? slides.map((slide, index) => {
      const button = document.createElement("button");
      const label = "Show " + slide.dataset.slide;
      button.type = "button";
      button.setAttribute("aria-label", label);
      button.title = label;
      button.addEventListener("click", () => show(index, true, index - current));
      selectors.append(button);
      return button;
    }) : [];

    function slideAt(offset) {
      return (current + offset + slides.length) % slides.length;
    }

    function updatePreview(button, offset) {
      if (!button) return;
      const slide = slides[slideAt(offset)];
      button.querySelector("img").src = slide.querySelector("img").src;
      button.setAttribute("aria-label", "Show " + slide.dataset.slide);
    }

    function updateControls() {
      updatePreview(previous, -1);
      updatePreview(next, 1);
      dots.forEach((dot, index) => dot.setAttribute("aria-current", String(index === current)));
    }

    function schedule() {
      clearTimeout(timer);
      if (playing && inView && !hovering && !document.hidden) {
        timer = setTimeout(() => show(slideAt(1)), interval);
      }
    }

    function setPlaying(value) {
      playing = value;
      const label = playing ? "Pause slideshow" : "Start slideshow";
      playback.setAttribute("aria-label", label);
      playback.title = label;
      playback.querySelector("use").setAttribute("href", playing ? "#pause" : "#play");
      schedule();
    }

    function show(index, manual = false, direction = 1) {
      if (index !== current) {
        slides[current].hidden = true;
        current = index;
        const slide = slides[current];
        slide.hidden = false;
        if (!motionPreference.matches) {
          const firstFrame = { opacity: .72 };
          if (previous) firstFrame.transform = direction > 0 ? "translateX(14px)" : "translateX(-14px)";
          slide.animate([firstFrame, { opacity: 1, transform: "none" }], { duration: 240, easing: "ease-out" });
        }
        updateControls();
      }
      if (manual) {
        status.textContent = slides[current].dataset.slide + ", " + (current + 1) + " of " + slides.length;
        setPlaying(false);
      } else {
        schedule();
      }
    }

    previous?.addEventListener("click", () => show(slideAt(-1), true, -1));
    next?.addEventListener("click", () => show(slideAt(1), true));
    playback.addEventListener("click", () => setPlaying(!playing));
    stage.addEventListener("keydown", event => {
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
      event.preventDefault();
      const direction = event.key === "ArrowLeft" ? -1 : 1;
      show(slideAt(direction), true, direction);
      if (dots.includes(event.target)) dots[current].focus();
    });
    stage.addEventListener("pointerenter", event => {
      if (event.pointerType === "touch") return;
      hovering = true;
      schedule();
    });
    stage.addEventListener("pointerleave", () => {
      hovering = false;
      schedule();
    });
    gallery.addEventListener("focusin", event => {
      if (event.target !== playback || playback.matches(":focus-visible")) setPlaying(false);
    });
    document.addEventListener("visibilitychange", schedule);
    motionPreference.addEventListener("change", event => {
      if (event.matches) setPlaying(false);
    });
    new IntersectionObserver(([entry]) => {
      inView = entry.intersectionRatio >= .25;
      schedule();
    }, { threshold: .25 }).observe(stage);

    updateControls();
    setPlaying(playing);
    if (previous && next) previous.hidden = next.hidden = false;
    const toolbar = gallery.querySelector(".slide-toolbar");
    if (toolbar) toolbar.hidden = false;
    playback.hidden = false;
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

    for (const gallery of document.querySelectorAll("[data-slideshow]")) initSlideshow(gallery);
  });
})();

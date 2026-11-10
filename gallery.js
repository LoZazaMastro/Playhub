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
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "dark" ? "#161616" : "#fff9ec");
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
    let actionMotion;
    let cardFade;

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
      if (playback) {
        playback.setAttribute("aria-label", label);
        playback.title = label;
        playback.querySelector("use").setAttribute("href", playing ? "#pause" : "#play");
      }
      schedule();
    }

    function slideAction(direction) {
      actionMotion?.remove();
      const motion = document.createElement("div");
      motion.className = "action-motion";
      motion.setAttribute("aria-hidden", "true");
      const track = document.createElement("div");
      track.className = "action-track";
      for (let offset = -2; offset <= 2; offset++) {
        const image = slides[slideAt(offset)].querySelector("img").cloneNode();
        image.loading = "eager";
        track.append(image);
      }
      motion.append(track);
      stage.append(motion);
      actionMotion = motion;

      const width = slides[current].getBoundingClientRect().width;
      const gap = Number.parseFloat(getComputedStyle(stage).columnGap);
      const distance = (width + gap) * direction;
      const timing = { duration: 440, easing: "cubic-bezier(.25,.7,.3,1)" };
      track.children[2].animate([{ opacity: 1 }, { opacity: .82 }], timing);
      track.children[2 + direction].animate([{ opacity: .82 }, { opacity: 1 }], timing);
      const animation = track.animate([
        { transform: "translateX(0)" },
        { transform: "translateX(" + -distance + "px)" }
      ], timing);
      animation.finished.then(() => motion.remove(), () => motion.remove());
    }

    function fadeCard(outgoing, incoming) {
      cardFade?.remove();
      const overlay = outgoing.querySelector("img").cloneNode();
      overlay.className = "card-fade-out";
      overlay.alt = "";
      overlay.setAttribute("aria-hidden", "true");
      overlay.loading = "eager";
      outgoing.parentElement.append(overlay);
      cardFade = overlay;

      const image = incoming.querySelector("img");
      image.loading = "eager";
      image.decode().then(() => {
        if (!overlay.isConnected) return;
        const animation = overlay.animate([{ opacity: 1 }, { opacity: 0 }], {
          duration: 420, easing: "ease-in-out", fill: "forwards"
        });
        animation.finished.then(() => overlay.remove(), () => overlay.remove());
      }, () => overlay.remove());
    }

    function show(index, manual = false, direction = 1) {
      if (index !== current) {
        if (!motionPreference.matches) {
          if (previous) slideAction(direction);
          else fadeCard(slides[current], slides[index]);
        }
        slides[current].hidden = true;
        current = index;
        slides[current].hidden = false;
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
    playback?.addEventListener("click", () => setPlaying(!playing));
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
      if (event.matches) {
        actionMotion?.remove();
        cardFade?.remove();
        setPlaying(false);
      }
    });
    new IntersectionObserver(([entry]) => {
      inView = entry.intersectionRatio >= .25;
      if (inView) {
        for (const slide of slides) slide.querySelector("img").loading = "eager";
      }
      schedule();
    }, { threshold: .25 }).observe(stage);

    updateControls();
    setPlaying(playing);
    if (previous && next) previous.hidden = next.hidden = false;
    const toolbar = gallery.querySelector(".slide-toolbar");
    if (toolbar) toolbar.hidden = false;
    if (playback) playback.hidden = false;
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

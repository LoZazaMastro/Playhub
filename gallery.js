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
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", theme === "dark" ? "#161616" : "#f7f7f7");
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
    const interval = 2000;
    let current = 0;
    let playing = !motionPreference.matches;
    let inView = false;
    let hovering = false;
    let timer;
    let actionMotion;
    let cardFade;
    let gesture;
    let cancelPointerGesture = () => {};
    let suppressClickUntil = 0;

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

    function makeActionTrack() {
      actionMotion?.remove();
      const motion = document.createElement("div");
      motion.className = "action-motion";
      motion.setAttribute("aria-hidden", "true");
      const track = document.createElement("div");
      track.className = "action-track";
      for (let offset = -2; offset <= 2; offset++) {
        const image = slides[slideAt(offset)].querySelector("img").cloneNode();
        image.loading = "eager";
        image.draggable = false;
        track.append(image);
      }
      track.children[2].style.opacity = "1";
      motion.append(track);
      stage.append(motion);
      actionMotion = motion;
      const width = slides[current].getBoundingClientRect().width;
      const gap = Number.parseFloat(getComputedStyle(stage).columnGap) || 0;
      return { motion, track, distance: width + gap };
    }

    function slideAction(direction) {
      const { motion, track, distance } = makeActionTrack();
      const timing = { duration: 440, easing: "cubic-bezier(.25,.7,.3,1)" };
      track.children[2].animate([{ opacity: 1 }, { opacity: .82 }], timing);
      track.children[2 + direction].animate([{ opacity: .82 }, { opacity: 1 }], timing);
      const animation = track.animate([
        { transform: "translateX(0)" },
        { transform: "translateX(" + -distance * direction + "px)" }
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

    function show(index, manual = false, direction = 1, dragRelease = false) {
      if (gesture && !dragRelease) cancelPointerGesture();
      if (index !== current) {
        if (!motionPreference.matches && !dragRelease) {
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

    // Touch and pointer dragging move the same full horizontal track used by
    // the controls. Vertical gestures remain native page scrolling.
    if (previous && next) {
      const release = pointerId => {
        try { if (stage.hasPointerCapture(pointerId)) stage.releasePointerCapture(pointerId); } catch {}
      };
      const cancelGesture = () => {
        if (!gesture) return;
        const old = gesture;
        gesture = undefined;
        old.motion?.remove();
        stage.classList.remove("is-dragging");
        release(old.pointerId);
        schedule();
      };
      cancelPointerGesture = cancelGesture;
      stage.addEventListener("pointerdown", event => {
        if (!event.isPrimary || event.button !== 0 || event.target.closest("a,input,select,textarea")) return;
        cancelGesture();
        clearTimeout(timer);
        gesture = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY,
          startTime: performance.now(), dx: 0, horizontal: false };
      });
      stage.addEventListener("pointermove", event => {
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        const dx = event.clientX - gesture.startX;
        const dy = event.clientY - gesture.startY;
        if (!gesture.horizontal) {
          if (Math.max(Math.abs(dx), Math.abs(dy)) < 8) return;
          if (Math.abs(dx) <= Math.abs(dy) * 1.15) { cancelGesture(); return; }
          gesture.horizontal = true;
          stage.setPointerCapture(event.pointerId);
          stage.classList.add("is-dragging");
          if (!motionPreference.matches) Object.assign(gesture, makeActionTrack());
        }
        event.preventDefault();
        gesture.dx = dx;
        if (gesture.track) {
          const travel = Math.max(-gesture.distance, Math.min(gesture.distance, dx));
          gesture.track.style.transform = "translateX(" + travel + "px)";
          const progress = Math.min(1, Math.abs(travel) / gesture.distance);
          gesture.track.children[2].style.opacity = String(1 - progress * .18);
          gesture.track.children[dx < 0 ? 3 : 1].style.opacity = String(.82 + progress * .18);
        }
      }, { passive: false });
      stage.addEventListener("pointerup", event => {
        if (!gesture || gesture.pointerId !== event.pointerId) return;
        const drag = gesture;
        gesture = undefined;
        stage.classList.remove("is-dragging");
        release(drag.pointerId);
        if (!drag.horizontal) { schedule(); return; }
        suppressClickUntil = performance.now() + 500;
        setPlaying(false);
        const width = slides[current].getBoundingClientRect().width;
        const elapsed = Math.max(1, performance.now() - drag.startTime);
        const advance = Math.abs(drag.dx) >= Math.min(80, width * .16)
          || (Math.abs(drag.dx) > 24 && Math.abs(drag.dx) / elapsed > .4);
        const direction = drag.dx < 0 ? 1 : -1;
        if (advance) show(slideAt(direction), true, direction, true);
        if (!drag.track || motionPreference.matches) { drag.motion?.remove(); return; }
        const destination = advance ? -direction * drag.distance : 0;
        const timing = { duration: advance ? 300 : 180, easing: "cubic-bezier(.25,.7,.3,1)", fill: "forwards" };
        drag.track.children[2].animate([{ opacity: drag.track.children[2].style.opacity || 1 }, { opacity: advance ? .82 : 1 }], timing);
        drag.track.children[2 + direction].animate([{ opacity: drag.track.children[2 + direction].style.opacity || .82 }, { opacity: advance ? 1 : .82 }], timing);
        const animation = drag.track.animate([
          { transform: drag.track.style.transform || "translateX(0)" },
          { transform: "translateX(" + destination + "px)" }
        ], timing);
        animation.finished.then(() => drag.motion.remove(), () => drag.motion.remove());
      });
      stage.addEventListener("pointercancel", cancelGesture);
      stage.addEventListener("lostpointercapture", event => {
        if (event.target === stage && gesture?.pointerId === event.pointerId) cancelGesture();
      });
      stage.addEventListener("click", event => {
        if (performance.now() < suppressClickUntil) { event.preventDefault(); event.stopImmediatePropagation(); }
      }, true);
      window.addEventListener("resize", cancelGesture);
      for (const image of stage.querySelectorAll("img")) image.draggable = false;
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
        cancelPointerGesture();
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

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
    if (!stage || slides.length < 2) return;
    const previous = gallery.querySelector("[data-action-previous]");
    const next = gallery.querySelector("[data-action-next]");
    const isAction = !!(previous && next);
    const status = gallery.querySelector(".slideshow-status");
    const motionPreference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const interval = 2000;
    let current = 0, revision = 0, settling = 0;
    let inView = false, hovering = false, focused = false;
    let timer, motion, gesture;
    let suppressClickUntil = 0;

    // Screens remain the complete image. Motion uses a temporary horizontal
    // reel above the settled image, including the full-size adjacent previews.
    gallery.querySelector(".slide-toolbar")?.remove();
    stage.tabIndex = 0;
    if (!stage.hasAttribute("role")) stage.setAttribute("role", "group");
    if (!stage.hasAttribute("aria-label")) stage.setAttribute("aria-label", gallery.getAttribute("aria-label") || "Screenshots");
    stage.setAttribute("aria-keyshortcuts", "ArrowLeft ArrowRight");
    for (const image of stage.querySelectorAll("img")) image.draggable = false;

    const slideAt = offset => (current + offset + slides.length) % slides.length;
    const canPlay = () => inView && !hovering && !focused && !gesture && !settling && !document.hidden && !motionPreference.matches;
    function stopTimer() { clearTimeout(timer); timer = undefined; }
    function schedule() {
      stopTimer();
      if (canPlay()) timer = setTimeout(() => { timer = undefined; void show(slideAt(1)); }, interval);
    }
    function finishMotion() {
      if (!motion) return;
      const old = motion; motion = undefined;
      for (const animation of old.animations || []) animation.cancel();
      old.element.remove();
    }
    function updateControls() {
      for (const [button, offset] of [[previous, -1], [next, 1]]) {
        if (!button) continue;
        const slide = slides[slideAt(offset)];
        const image = button.querySelector("img");
        if (image) image.src = slide.querySelector("img").src;
        button.setAttribute("aria-label", "Show " + slide.dataset.slide);
      }
      stage.dataset.currentSlide = String(current);
    }
    function commit(index, manual) {
      slides[current].hidden = true;
      current = index;
      slides[current].hidden = false;
      updateControls();
      if (manual && status) status.textContent = slides[current].dataset.slide + ", " + (current + 1) + " of " + slides.length;
    }
    async function readyImage(image) {
      image.loading = "eager";
      try { await image.decode(); } catch { /* Keep the settled image when a requested image is unavailable. */ }
      return image.complete && image.naturalWidth > 0;
    }
    const ready = index => readyImage(slides[index].querySelector("img"));
    function makeTrack() {
      finishMotion();
      const element = document.createElement("div");
      const track = document.createElement("div");
      element.className = isAction ? "action-motion" : "card-motion";
      track.className = isAction ? "action-track" : "card-track";
      element.setAttribute("aria-hidden", "true");
      element.style.visibility = "hidden";
      const center = isAction ? 2 : 1;
      for (let offset = -center; offset <= center; offset++) {
        const image = slides[slideAt(offset)].querySelector("img").cloneNode();
        image.alt = ""; image.loading = "eager"; image.draggable = false;
        track.append(image);
      }
      if (isAction) track.children[center].style.opacity = "1";
      element.append(track); stage.append(element);
      const width = slides[current].getBoundingClientRect().width;
      const gap = isAction ? Number.parseFloat(getComputedStyle(stage).columnGap) || 0 : 0;
      const reel = { element, track, center, distance: width + gap };
      reel.ready = Promise.all(Array.from(track.children, readyImage)).then(loaded => {
        const decoded = loaded.every(Boolean);
        if (decoded && motion === reel) element.style.visibility = "";
        return decoded;
      });
      motion = reel;
      return reel;
    }
    function updateTrackOpacity(reel, travel) {
      if (!isAction) return;
      const progress = Math.min(1, Math.abs(travel) / reel.distance);
      reel.track.children[reel.center].style.opacity = String(1 - progress * .18);
      reel.track.children[reel.center + (travel < 0 ? 1 : -1)].style.opacity = String(.82 + progress * .18);
    }
    function animateTrack(reel, from, to, duration = 440) {
      const timing = { duration, easing: "cubic-bezier(.25,.7,.3,1)", fill: "forwards" };
      reel.animations = [];
      if (isAction) {
        const center = reel.track.children[reel.center];
        const adjacent = reel.track.children[reel.center + ((to || from) < 0 ? 1 : -1)];
        reel.animations.push(center.animate([{ opacity: center.style.opacity || 1 }, { opacity: to === 0 ? 1 : .82 }], timing));
        reel.animations.push(adjacent.animate([{ opacity: adjacent.style.opacity || .82 }, { opacity: to === 0 ? .82 : 1 }], timing));
      }
      const animation = reel.track.animate([
        { transform: "translateX(" + from + "px)" },
        { transform: "translateX(" + to + "px)" }
      ], timing);
      reel.animations.push(animation);
      animation.finished.then(() => {
        if (motion === reel) motion = undefined;
        reel.element.remove();
      }, () => reel.element.remove());
    }
    function release(pointerId) {
      try { if (stage.hasPointerCapture(pointerId)) stage.releasePointerCapture(pointerId); } catch {}
    }
    function cancelGesture() {
      if (!gesture) return;
      const old = gesture; gesture = undefined;
      stage.classList.remove("is-dragging");
      release(old.pointerId); finishMotion(); schedule();
    }
    async function show(index, manual = false, direction = 1) {
      if (gesture) cancelGesture();
      if (settling) finishMotion();
      stopTimer();
      const request = ++revision;
      settling = request;
      const decoded = await ready(index);
      if (request !== revision) return;
      const reel = decoded && index !== current && !motionPreference.matches ? makeTrack() : undefined;
      const reelReady = !reel || await reel.ready;
      if (request !== revision) return;
      settling = 0;
      if (!decoded || !reelReady || (!manual && !canPlay())) { finishMotion(); schedule(); return; }
      if (index !== current) {
        commit(index, manual);
        if (reel) animateTrack(reel, 0, -Math.sign(direction) * reel.distance);
      }
      schedule();
    }

    // Only an established horizontal gesture is captured. A vertical gesture
    // remains normal page scrolling; a tap keeps its original click behavior.
    stage.addEventListener("pointerdown", event => {
      if (!event.isPrimary || event.button !== 0 || event.target.closest("a,input,select,textarea")) return;
      cancelGesture(); stopTimer(); ++revision; settling = 0; finishMotion();
      gesture = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY,
        startTime: performance.now(), dx: 0, horizontal: false };
    });
    stage.addEventListener("pointermove", event => {
      if (!gesture || gesture.pointerId !== event.pointerId) return;
      const dx = event.clientX - gesture.startX, dy = event.clientY - gesture.startY;
      if (!gesture.horizontal) {
        if (Math.max(Math.abs(dx), Math.abs(dy)) < 8) return;
        if (Math.abs(dx) <= Math.abs(dy) * 1.15) { cancelGesture(); return; }
        gesture.horizontal = true;
        stage.setPointerCapture(event.pointerId); stage.classList.add("is-dragging");
        if (!motionPreference.matches) gesture.reel = makeTrack();
      }
      event.preventDefault(); gesture.dx = dx;
      if (gesture.reel) {
        const travel = Math.max(-gesture.reel.distance, Math.min(gesture.reel.distance, dx));
        gesture.reel.track.style.transform = "translateX(" + travel + "px)";
        updateTrackOpacity(gesture.reel, travel);
      }
    }, { passive: false });
    stage.addEventListener("pointerup", event => {
      if (!gesture || gesture.pointerId !== event.pointerId) return;
      const drag = gesture; gesture = undefined;
      stage.classList.remove("is-dragging"); release(drag.pointerId);
      if (!drag.horizontal) { schedule(); return; }
      suppressClickUntil = performance.now() + 500;
      const width = slides[current].getBoundingClientRect().width;
      const elapsed = Math.max(1, performance.now() - drag.startTime);
      const advance = Math.abs(drag.dx) >= Math.min(80, width * .16)
        || (Math.abs(drag.dx) > 24 && Math.abs(drag.dx) / elapsed > .4);
      const direction = drag.dx < 0 ? 1 : -1;
      const target = slideAt(direction), request = ++revision;
      settling = request; stopTimer();
      void (async () => {
        const decoded = !advance || await ready(target);
        const reelReady = !drag.reel || await drag.reel.ready;
        if (request !== revision) return;
        settling = 0;
        const accepted = advance && decoded && reelReady;
        if (accepted) commit(target, true);
        if (drag.reel && reelReady && !motionPreference.matches) {
          const travel = Math.max(-drag.reel.distance, Math.min(drag.reel.distance, drag.dx));
          animateTrack(drag.reel, travel, accepted ? -direction * drag.reel.distance : 0, accepted ? 300 : 180);
        } else finishMotion();
        schedule();
      })();
    });
    stage.addEventListener("pointercancel", cancelGesture);
    stage.addEventListener("lostpointercapture", event => {
      if (event.target === stage && gesture?.pointerId === event.pointerId) cancelGesture();
    });
    stage.addEventListener("click", event => {
      if (performance.now() < suppressClickUntil) { event.preventDefault(); event.stopImmediatePropagation(); }
    }, true);
    previous?.addEventListener("click", () => { void show(slideAt(-1), true, -1); });
    next?.addEventListener("click", () => { void show(slideAt(1), true, 1); });
    stage.addEventListener("keydown", event => {
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
      if (event.target.closest("input,select,textarea")) return;
      event.preventDefault();
      const direction = event.key === "ArrowLeft" ? -1 : 1;
      void show(slideAt(direction), true, direction);
    });
    stage.addEventListener("pointerenter", event => {
      if (event.pointerType !== "touch") { hovering = true; schedule(); }
    });
    stage.addEventListener("pointerleave", () => { hovering = false; schedule(); });
    gallery.addEventListener("focusin", () => { focused = true; schedule(); });
    gallery.addEventListener("focusout", () => {
      queueMicrotask(() => { focused = gallery.contains(document.activeElement); schedule(); });
    });
    document.addEventListener("visibilitychange", () => {
      if (document.hidden) { ++revision; settling = 0; cancelGesture(); finishMotion(); }
      schedule();
    });
    motionPreference.addEventListener("change", () => {
      ++revision; settling = 0; cancelGesture(); finishMotion(); schedule();
    });
    window.addEventListener("resize", () => { ++revision; settling = 0; cancelGesture(); finishMotion(); schedule(); });
    new IntersectionObserver(([entry]) => {
      inView = entry.intersectionRatio >= .25;
      if (!inView) { ++revision; settling = 0; cancelGesture(); finishMotion(); }
      if (inView) for (const slide of slides) slide.querySelector("img").loading = "eager";
      schedule();
    }, { threshold: .25 }).observe(stage);
    updateControls(); schedule();
    if (isAction) previous.hidden = next.hidden = false;
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

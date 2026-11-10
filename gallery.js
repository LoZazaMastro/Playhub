const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

for (const gallery of document.querySelectorAll("[data-gallery]")) {
  const track = gallery.querySelector(".app-gallery-track");
  const controls = gallery.querySelector(".gallery-controls");
  const previous = controls.querySelector("[data-gallery-previous]");
  const next = controls.querySelector("[data-gallery-next]");
  const slides = [...track.children];

  function maxScroll() {
    return track.scrollWidth - track.clientWidth;
  }

  function slideOffsets() {
    const firstOffset = slides[0].offsetLeft;
    return slides.map(slide => Math.min(slide.offsetLeft - firstOffset, maxScroll()));
  }

  function move(direction) {
    const offsets = slideOffsets();
    const destination = direction > 0
      ? offsets.find(offset => offset > track.scrollLeft + 2)
      : offsets.findLast(offset => offset < track.scrollLeft - 2);
    if (destination === undefined) return;
    track.scrollTo({ left: destination, behavior: reducedMotion.matches ? "instant" : "smooth" });
  }

  function updateControls() {
    controls.hidden = maxScroll() < 2;
    previous.disabled = track.scrollLeft < 2;
    next.disabled = track.scrollLeft >= maxScroll() - 2;
  }

  previous.addEventListener("click", () => move(-1));
  next.addEventListener("click", () => move(1));
  track.addEventListener("scroll", updateControls, { passive: true });
  track.addEventListener("keydown", event => {
    if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
    event.preventDefault();
    move(event.key === "ArrowRight" ? 1 : -1);
  });
  new ResizeObserver(updateControls).observe(track);
  updateControls();
}

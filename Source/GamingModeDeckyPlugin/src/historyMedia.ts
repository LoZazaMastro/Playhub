export type EditorialImage = { file: string; url: string; subject: string; kind?: string };
export type ImageBinding = { subject?: string; image_subject?: string; image_file?: string; image_role?: string };

/** Explicit editorial assignments take precedence over positional fallbacks. */
export function orderHistoryImages(all: EditorialImage[], chapter: ImageBinding | undefined, index: number,
  chapters?: ImageBinding[], intro?: ImageBinding, digest?: (image: EditorialImage) => string | undefined): EditorialImage[] {
  if (chapters) {
    const key = (image: EditorialImage) => digest?.(image) || image.file;
    const used = new Set<string>();
    const bindings = [intro, ...chapters];
    const candidates = (at: number, assigned: Set<string>) => {
      const binding = bindings[at];
      const reserved = new Set(bindings.slice(at + 1).map(value => value?.image_file).filter(Boolean));
      const reservedContent = binding?.image_file ? undefined : new Set(all.filter(image => reserved.has(image.file)).map(key));
      return orderHistoryImages(all.filter(image =>
        !assigned.has(key(image)) && !reserved.has(image.file) && !reservedContent?.has(key(image))), binding, 0);
    };
    for (let current = 0; current < bindings.length; current++) {
      const ordered = candidates(current, used);
      const selected = ordered[0];
      if (current === index) {
        const laterUsed = new Set(used);
        if (selected) laterUsed.add(key(selected));
        for (let later = current + 1; later < bindings.length; later++) {
          const next = candidates(later, laterUsed)[0];
          if (next) laterUsed.add(key(next));
        }
        return ordered.filter(image => image === selected || !laterUsed.has(key(image)));
      }
      if (selected) used.add(key(selected));
    }
    return [];
  }
  if (!chapter && index === 0 && intro) chapter = intro;
  const subject = chapter?.image_subject ?? chapter?.subject;
  const candidates = all.filter(image =>
    (!subject || image.subject === subject) &&
    (!chapter?.image_file || image.file === chapter.image_file) &&
    (chapter?.image_role === 'cover' || (image.kind !== 'cover' && !/images\.igdb\.com\/.*\/co\w+\./.test(image.url))));
  if (chapter?.image_file) return candidates;
  const matching = chapter?.image_role ? candidates.filter(image => image.kind === chapter.image_role) : [];
  if (matching.length) return matching;
  const offset = index % Math.max(candidates.length, 1);
  return [...candidates.slice(offset), ...candidates.slice(0, offset)];
}


/** Covers belong to the release heading, independently of the story photographs. */
export function historyHeadingCover(all: EditorialImage[], kind?: string, coverFile?: string): EditorialImage | undefined {
  const gameRelease = /^(game|release(?:_regional)?|uscita(?:_(?:regionale|mondiale|multiregionale|internazionale|digitale|espansione))?|prima_versione_pubblica|rilascio_shareware|accesso_anticipato)$/.test(kind ?? '');
  if (!gameRelease && !coverFile) return undefined;
  return coverFile ? all.find(image => image.file === coverFile) : all.find(image => image.kind === 'cover');
}

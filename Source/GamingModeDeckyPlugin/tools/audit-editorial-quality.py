"""Report editorial repetition and incomplete locales before publishing a workbook.

This is an evidence report, not an automatic rewrite or factual certification.
All paths are explicit so reports can stay outside the distributable plugin.
"""
import argparse
from collections import defaultdict
import json
import re


LANGUAGES = ('it', 'en', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi')


def audit(document, languages=LANGUAGES):
    passages = defaultdict(set)
    findings = []
    topics = defaultdict(set)
    stories = document.get('stories', [])
    for story in stories:
        date = story['date']
        topics[story['topic']].add(date)
        fields = [('title', story['title']), ('intro', story['intro'])]
        for chapter in story['chapters']:
            fields.extend((f"chapter-{chapter['order']}/{field}", chapter[field])
                          for field in ('kicker', 'title', 'body'))
        for path, localized in fields:
            for language in languages:
                text = localized.get(language, '')
                if not text.strip():
                    findings.append(dict(date=date, field=path, language=language, issue='missing_translation'))
                    continue
                if '\u2014' in text:
                    findings.append(dict(date=date, field=path, language=language, issue='long_dash'))
                if path == 'intro' or path.endswith('/body'):
                    # Sentence boundaries include CJK punctuation; retain spaces
                    # within sentences so the report remains human-readable.
                    for sentence in re.split(r'(?<=[.!?。！？])(?:\s+|(?=[^\x00-\x7f]))', text):
                        if len(sentence) >= 55:
                            passages[(language, sentence.strip())].add(date)
    repetitions = [dict(language=lang, text=text, dates=sorted(dates))
                   for (lang, text), dates in passages.items() if len(dates) >= 3]
    repetitions.sort(key=lambda item: (-len(item['dates']), item['language'], item['text']))
    affected = sorted({date for item in repetitions for date in item['dates']})
    return dict(story_count=len(stories), findings=findings, repeated_passages=repetitions,
                dates_with_repetition=affected,
                duplicate_topics=[dict(topic=topic, dates=sorted(dates))
                                  for topic, dates in topics.items() if len(dates) > 1])


if __name__ == '__main__':
    from pathlib import Path
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--languages', nargs='+', choices=LANGUAGES, default=LANGUAGES)
    args = parser.parse_args()
    report = audit(json.loads(args.input.read_text(encoding='utf-8-sig')), args.languages)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items()
                      if key not in ('findings', 'repeated_passages', 'dates_with_repetition')}, ensure_ascii=False))
    print('Dates with repeated passages:', len(report['dates_with_repetition']))

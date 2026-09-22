"""Merge written workbook stories; never replace editions with candidate rows.

Images are explicit reviewed assignments. Unassigned cards remain text-only and
are recorded in missing_media instead of borrowing a cover or another subject.
"""
import argparse
import copy
import hashlib
import json
import re
from pathlib import Path

LANGUAGES = ('it', 'en', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi')
KINDS = {'nascita': 'birth', 'birth': 'birth', 'foundation': 'foundation',
         'fondazione': 'foundation', 'release_regional': 'release', 'public_debut': 'commemoration',
         'uscita': 'release', 'uscita_regionale': 'release', 'release': 'release',
         'debutto': 'commemoration', 'annuncio': 'commemoration',
         'annuncio_hardware': 'commemoration', 'annuncio_disponibilita': 'commemoration',
         'presentazione': 'commemoration', 'acquisizione': 'commemoration',
         'ricorrenza_globale': 'commemoration', 'global_observance': 'commemoration',
         'ricorrenza': 'commemoration', 'evento': 'commemoration',
         'debutto_tecnologia': 'commemoration', 'lancio_hardware': 'release',
         'uscita_hardware_software': 'release', 'commemoration': 'commemoration', 'hardware': 'release'}
SUBJECT_KINDS = {'videogioco': 'game', 'game': 'game', 'serie': 'history', 'console': 'hardware',
                 'autore': 'creator', 'creator': 'creator', 'studio': 'company', 'company': 'company', 'azienda': 'company',
                 'evento': 'history', 'ricorrenza': 'history', 'tecnologia': 'history',
                 'gioco': 'game', 'persona': 'creator', 'hardware': 'hardware'}
HEADLINES = {
 'public_debut': ['In questo giorno debutta in pubblico', 'Public debut on this day', 'Öffentliches Debüt an diesem Tag', 'Debut público este día', 'Première présentation publique ce jour-là', 'Estreia pública neste dia', 'Публичный дебют в этот день', 'Публічний дебют цього дня', 'この日に一般公開', '이날 대중에게 첫 공개', '于这一天首次公开亮相', 'इस दिन सार्वजनिक शुरुआत हुई'],
 'announcement': ['In questo giorno viene annunciato', 'Announced on this day', 'An diesem Tag angekündigt', 'Anunciado este día', 'Annoncé ce jour-là', 'Anunciado neste dia', 'Анонс в этот день', 'Анонс цього дня', 'この日に発表', '이날 발표', '于这一天公布', 'इस दिन घोषणा हुई'],
 'availability': ['In questo giorno viene annunciata la disponibilità', 'Availability announced on this day', 'Verfügbarkeit an diesem Tag angekündigt', 'Disponibilidad anunciada este día', 'Disponibilité annoncée ce jour-là', 'Disponibilidade anunciada neste dia', 'В этот день объявлено о доступности', 'Цього дня оголошено про доступність', 'この日に発売を告知', '이날 판매 개시 발표', '于这一天宣布上市', 'इस दिन उपलब्धता की घोषणा हुई'],
 'debut': ['In questo giorno debutta la beta pubblica', 'Public beta launched on this day', 'Start der öffentlichen Beta', 'Debut de la beta pública', 'Début de la bêta publique', 'Estreia da beta pública', 'Запуск открытой беты', 'Запуск відкритої бети', '公開ベータ開始の日', '공개 베타 시작일', '公开测试开启日', 'सार्वजनिक बीटा की शुरुआत'],
 'presentation': ['Presentazione del 12 gennaio in Nord America', 'January 12 presentation in North America', 'Präsentation am 12. Januar in Nordamerika', 'Presentación del 12 de enero en Norteamérica', 'Présentation du 12 janvier en Amérique du Nord', 'Apresentação de 12 de janeiro na América do Norte', 'Презентация 12 января в Северной Америке', 'Презентація 12 січня в Північній Америці', '北米では1月12日の発表会', '북미 기준 1월 12일 발표회', '北美时间1月12日发布会', 'उत्तरी अमेरिका में 12 जनवरी की प्रस्तुति'],
 'acquisition': ['In questo giorno viene annunciata l’acquisizione', 'Acquisition announced on this day', 'Übernahme an diesem Tag angekündigt', 'Adquisición anunciada este día', 'Acquisition annoncée ce jour-là', 'Aquisição anunciada neste dia', 'В этот день объявлено о приобретении', 'Цього дня оголошено про придбання', 'この日に買収を発表', '이날 인수 발표', '于这一天宣布收购', 'इस दिन अधिग्रहण की घोषणा हुई'],
 'newyear': ['Capodanno nel villaggio', 'New Year’s Day in the village', 'Neujahr im Dorf', 'Año Nuevo en el pueblo', 'Nouvel An au village', 'Ano-Novo na vila', 'Новый год в деревне', 'Новий рік у селищі', '村のお正月', '마을의 새해 첫날', '村庄里的元旦', 'गांव में नए साल का दिन'],
 'wetlands': ['Giornata mondiale delle zone umide', 'World Wetlands Day', 'Welttag der Feuchtgebiete', 'Día Mundial de los Humedales', 'Journée mondiale des zones humides', 'Dia Mundial das Zonas Húmidas', 'Всемирный день водно-болотных угодий', 'Всесвітній день водно-болотних угідь', '世界湿地の日', '세계 습지의 날', '世界湿地日', 'विश्व आर्द्रभूमि दिवस'],
}


def validate_media(plan, assets):
    for date, assignment in plan.items():
        seen = set()
        for slot, image in [('hero', assignment.get('hero')), *assignment.get('chapters', {}).items(), ('cover', assignment.get('cover'))]:
            if not image:
                continue
            filename = image['file']
            if Path(filename).name != filename or not image.get('subject'):
                raise ValueError(f'Invalid image binding: {date}/{slot}')
            if slot != 'cover' and image.get('kind') == 'cover':
                raise ValueError(f'Cover assigned to editorial card: {date}/{slot}')
            digest = hashlib.sha256((assets / filename).read_bytes()).hexdigest()
            if digest in seen:
                raise ValueError(f'Repeated image in story: {date}/{slot}')
            seen.add(digest)


def integrate(existing, extracted, media_plan):
    result = copy.deepcopy(existing)
    entries = result.setdefault('entries', [])
    for story in extracted['stories']:
        if story['source_status'] != 'PRONTO' or not story['complete_languages']:
            continue
        date, topic = story['date'], story['topic']
        identity = 'workbook-' + date + '-' + re.sub(r'[^a-z0-9]+', '-', topic.lower()).strip('-')
        media_id = 'step06-' + date
        assignments = media_plan.get(date, {})
        old = next((e for e in entries if e['theme'].get('topic') == topic
                    and e.get('anniversary', {}).get('date') == date), None)
        if old and old.get('published') is True and story.get('publication_ready') is False:
            # A newer workbook can carry an unfinished revision of an already
            # approved article. Staging that draft must not unpublish the live one.
            continue
        if old:
            identity = old['theme']['id']
        theme = dict(id=identity, media_id=media_id, subject=story['subject'], topic=topic,
                     kind=SUBJECT_KINDS[story['subject_type']], curated_edition=True,
                     title=story['title'], intro=story['intro'], chapters=[], sources=story['source_urls'])
        missing = []
        if not assignments.get('hero'):
            missing.append(dict(order=0, subject=story['subject'], brief='Opening image of the subject'))
        for chapter in story['chapters']:
            assigned = assignments.get('chapters', {}).get(str(chapter['order']))
            # A deliberately unresolved binding must not trigger positional fallbacks.
            image_file = assigned['file'] if assigned else '__pending__'
            theme['chapters'].append(dict(subject=assigned['subject'] if assigned else chapter['subject'],
                kicker=chapter['kicker'], title=chapter['title'], body=chapter['body'],
                image_file=image_file, image_role=assigned.get('kind', 'editorial') if assigned else 'editorial'))
            if not assigned:
                missing.append(dict(order=chapter['order'], subject=chapter['subject'], brief=chapter['image_brief']))
        anniversary = dict(date=date, kind=KINDS[story['kind']])
        if story['kind'] not in ('ricorrenza', 'ricorrenza_globale', 'global_observance'):
            anniversary['year'] = story['year']
        header_key = {'annuncio': 'announcement', 'annuncio_hardware': 'announcement',
                      'annuncio_disponibilita': 'availability', 'presentazione': 'public_debut',
                      'acquisizione': 'acquisition', 'debutto': 'public_debut',
                      'debutto_tecnologia': 'public_debut', 'public_debut': 'public_debut'}.get(story['kind'])
        # These labels describe individual anniversaries, not whole event kinds.
        if date == '01-12' and topic == 'Nintendo Switch' and story['kind'] == 'presentazione':
            header_key = 'presentation'
        elif date == '01-04' and topic == 'RuneScape' and story['kind'] == 'debutto':
            header_key = 'debut'
        elif date == '01-01' and story['kind'] == 'ricorrenza_globale':
            header_key = 'newyear'
        elif date == '02-02' and story['kind'] == 'global_observance':
            header_key = 'wetlands'
        if header_key:
            anniversary['headline'] = dict(zip(LANGUAGES, HEADLINES[header_key]))
        if story.get('headline'):
            anniversary['headline'] = copy.deepcopy(story['headline'])
        if not assignments.get('cover') and theme['kind'] == 'game':
            missing.append(dict(order='cover', subject=story['subject'], brief='Game cover for the header only'))
        entry = dict(published=story.get('publication_ready', True), locales=story['complete_languages'], priority=story['priority'],
                     theme=theme, anniversary=anniversary,
                     media_status='incomplete' if missing else 'complete', missing_media=missing,
                     provenance=dict(workbook=extracted.get('source'), row=story['source_row'],
                                     occasion=story['occasion'], verification_note=story['verification_note'],
                                     chapters=[dict(order=c['order'], row=c['source_row'], source=c['source_url']) for c in story['chapters']]))
        entries[:] = [e for e in entries if e['theme']['id'] != identity]
        entries.append(entry)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extracted', type=Path)
    parser.add_argument('--existing', type=Path, required=True)
    parser.add_argument('--media-plan', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    read = lambda p: json.loads(p.read_text('utf-8-sig'))
    media = read(args.media_plan) if args.media_plan else {}
    validate_media(media, Path(__file__).resolve().parents[1] / 'src/assets/history')
    result = integrate(read(args.existing), read(args.extracted), media)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Editions:', len(result['entries']))

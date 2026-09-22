#!/usr/bin/env node
// One-off, repeatable editorial migration for the Hideo Kojima feature.
// Keeping this as a script makes the sizeable multilingual JSON change auditable.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const editorialPath = path.join(root, 'quick_settings', 'history_editorial.json');
const imagesPath = path.join(root, 'quick_settings', 'history_images.json');
const editorial = JSON.parse(fs.readFileSync(editorialPath, 'utf8'));
const images = JSON.parse(fs.readFileSync(imagesPath, 'utf8'));
const theme = editorial.themes.find((item) => item.id === 'kojima');
if (!theme) throw new Error('Kojima theme not found');
if (!Array.isArray(theme.chapters) || theme.chapters.length < 2) throw new Error('Kojima source chapters are incomplete');

const metalGearSolid = theme.chapters.find(c => c.subject === 'Metal Gear Solid') ?? theme.chapters[0];
const deathStranding = theme.chapters.find(c => c.subject === 'Death Stranding') ?? theme.chapters[1];
metalGearSolid.subject = 'Metal Gear Solid';
metalGearSolid.kicker = Object.fromEntries(Object.keys(metalGearSolid.title).map(lang=>[lang,'METAL GEAR SOLID']));
metalGearSolid.image_role = 'game';
metalGearSolid.image_file = 'metalgear-0.jpg';
deathStranding.subject = 'Death Stranding';
deathStranding.kicker = Object.fromEntries(Object.keys(deathStranding.title).map(lang=>[lang,'DEATH STRANDING']));
deathStranding.image_role = 'game';
deathStranding.image_file = 'death-stranding-game.png';

const youth = {
  subject: 'Hideo Kojima',
  kicker: 'HIDEO KOJIMA · GLI ANNI DELLA FORMAZIONE',
  image_role: 'portrait',
  image_file: 'kojima-youth.png',
  title: {
    it: 'Prima dei videogiochi, le immagini', en: 'Before video games, images', de: 'Vor den Videospielen kamen die Bilder',
    es: 'Antes de los videojuegos, las imágenes', fr: 'Avant les jeux vidéo, les images', pt: 'Antes dos videojogos, as imagens',
    ru: 'До видеоигр были образы', uk: 'До відеоігор були образи', ja: 'ゲームより先に、映像があった',
    ko: '게임보다 먼저 이미지가 있었다', zh: '在电子游戏之前，先有影像', hi: 'वीडियो गेम से पहले, छवियाँ थीं'
  },
  body: {
    it: 'Nato a Tokyo nel 1963 e cresciuto nel Kansai, Kojima incontra presto il cinema attraverso le serate in famiglia. Vorrebbe disegnare, scrivere e dirigere film; la perdita del padre durante l’adolescenza rende quel desiderio più difficile, senza cancellarlo. Quando sceglie i videogiochi porta con sé quella educazione allo sguardo: il montaggio, il fuori campo e il ritmo diventeranno strumenti interattivi.',
    en: 'Born in Tokyo in 1963 and raised in Kansai, Kojima encountered cinema early through family movie nights. He wanted to draw, write and direct films; losing his father as a teenager made that ambition harder without erasing it. When he chose video games, he carried that visual education with him: editing, off-screen space and rhythm would become interactive tools.',
    de: '1963 in Tokio geboren und in Kansai aufgewachsen, begegnete Kojima dem Kino früh bei Filmabenden mit seiner Familie. Er wollte zeichnen, schreiben und Filme drehen. Der Tod seines Vaters in seiner Jugend erschwerte diesen Wunsch, löschte ihn aber nicht aus. In die Videospiele nahm er Montage, Bildraum und Rhythmus als interaktive Werkzeuge mit.',
    es: 'Nacido en Tokio en 1963 y criado en Kansai, Kojima conoció pronto el cine durante las veladas familiares. Quería dibujar, escribir y dirigir películas; perder a su padre en la adolescencia complicó ese deseo sin borrarlo. Al elegir los videojuegos llevó consigo esa educación visual: el montaje, el fuera de campo y el ritmo se convertirían en herramientas interactivas.',
    fr: 'Né à Tokyo en 1963 et élevé dans le Kansai, Kojima découvre tôt le cinéma lors des soirées familiales. Il veut dessiner, écrire et réaliser des films ; la perte de son père à l’adolescence rend ce désir plus difficile sans l’effacer. En choisissant le jeu vidéo, il emporte cette éducation du regard : montage, hors-champ et rythme deviennent des outils interactifs.',
    pt: 'Nascido em Tóquio em 1963 e criado em Kansai, Kojima encontrou cedo o cinema nas noites em família. Queria desenhar, escrever e realizar filmes; perder o pai na adolescência tornou esse desejo mais difícil sem o apagar. Ao escolher os videojogos levou consigo essa educação visual: montagem, fora de campo e ritmo tornaram-se ferramentas interativas.',
    ru: 'Кодзима родился в Токио в 1963 году и вырос в регионе Кансай. С кино он познакомился на семейных просмотрах. Он хотел рисовать, писать и снимать фильмы; смерть отца в подростковом возрасте усложнила этот путь, но не уничтожила мечту. В видеоигры он принёс монтаж, пространство за кадром и ритм как интерактивные инструменты.',
    uk: 'Кодзіма народився в Токіо 1963 року й виріс у регіоні Кансай. Із кіно він познайомився на сімейних переглядах. Він хотів малювати, писати й знімати фільми; смерть батька в підлітковому віці ускладнила цей шлях, але не знищила мрію. У відеоігри він приніс монтаж, простір за кадром і ритм як інтерактивні інструменти.',
    ja: '1963年に東京で生まれ、関西で育った小島秀夫は、家族で映画を見る時間を通じて早くから映像に親しんだ。絵を描き、物語を書き、映画を撮りたいと願ったが、思春期に父を失い、その道は容易ではなくなる。それでも夢は消えず、ゲームの世界へ編集、画面外の想像力、リズムという映画の感覚を持ち込んだ。',
    ko: '1963년 도쿄에서 태어나 간사이에서 자란 고지마 히데오는 가족 영화 감상을 통해 일찍 영상과 만났다. 그림을 그리고 글을 쓰며 영화를 만들고 싶었지만, 십 대에 아버지를 잃으며 그 길은 어려워졌다. 꿈은 사라지지 않았고, 그는 편집과 화면 밖 공간, 리듬을 상호작용의 도구로 게임에 가져왔다.',
    zh: '小岛秀夫1963年生于东京，在关西长大，并在家庭观影中很早接触电影。他曾想画画、写作和执导电影；少年时期失去父亲让这条路变得艰难，却没有抹去愿望。进入电子游戏领域后，他把剪辑、画外空间与节奏转化为互动工具。',
    hi: '1963 में टोक्यो में जन्मे और कंसाई में पले कोजिमा ने पारिवारिक फ़िल्म-रातों से सिनेमा को जल्दी जाना। वे चित्र बनाना, लिखना और फ़िल्म निर्देशित करना चाहते थे; किशोरावस्था में पिता की मृत्यु ने राह कठिन की, सपना नहीं मिटाया। गेम चुनते समय वे संपादन, दृश्य से बाहर की जगह और लय को इंटरैक्टिव औज़ार बनाकर साथ लाए।'
  }
};

const method = {
  subject: 'Hideo Kojima',
  kicker: 'HIDEO KOJIMA · IL METODO',
  image_role: 'portrait',
  image_file: 'kojima-cinematic.png',
  title: {
    it: 'L’autore entra nell’inquadratura', en: 'The author enters the frame', de: 'Der Autor tritt ins Bild',
    es: 'El autor entra en el encuadre', fr: 'L’auteur entre dans le cadre', pt: 'O autor entra no enquadramento',
    ru: 'Автор входит в кадр', uk: 'Автор входить у кадр', ja: '作者がフレームに入る',
    ko: '작가가 프레임 안으로 들어오다', zh: '作者走入镜头', hi: 'लेखक फ़्रेम में प्रवेश करता है'
  },
  body: {
    it: 'Kojima lavora come un regista che non dimentica mai il controller. Attori, musica, grafica, montaggio e sistemi di gioco vengono pensati come parti della stessa regia. Le sue campagne fotografiche rendono visibile questo metodo: l’autore non si limita a firmare l’opera, costruisce intorno a essa un immaginario riconoscibile e invita cinema, moda e musica dentro la produzione del videogioco.',
    en: 'Kojima works like a director who never forgets the controller. Actors, music, visual design, editing and game systems are treated as parts of the same direction. His photographic campaigns make that method visible: the author does more than sign the work, building a recognizable world around it and inviting cinema, fashion and music into game production.',
    de: 'Kojima arbeitet wie ein Regisseur, der den Controller nie vergisst. Schauspiel, Musik, Gestaltung, Montage und Spielsysteme gehören zur selben Inszenierung. Seine Fotokampagnen machen diese Methode sichtbar: Der Autor signiert nicht nur ein Werk, sondern baut eine erkennbare Welt darum und holt Kino, Mode und Musik in die Spieleproduktion.',
    es: 'Kojima trabaja como un director que nunca olvida el mando. Actores, música, diseño visual, montaje y sistemas de juego forman parte de una misma puesta en escena. Sus campañas fotográficas hacen visible ese método: el autor no solo firma la obra, construye un imaginario reconocible e invita al cine, la moda y la música a la producción del videojuego.',
    fr: 'Kojima travaille comme un réalisateur qui n’oublie jamais la manette. Acteurs, musique, graphisme, montage et systèmes de jeu relèvent d’une même mise en scène. Ses campagnes photographiques rendent cette méthode visible : l’auteur ne signe pas seulement l’œuvre, il construit autour d’elle un imaginaire reconnaissable et invite cinéma, mode et musique dans la production.',
    pt: 'Kojima trabalha como um realizador que nunca esquece o comando. Atores, música, grafismo, montagem e sistemas de jogo pertencem à mesma encenação. As campanhas fotográficas tornam o método visível: o autor não se limita a assinar a obra, constrói um imaginário reconhecível e convida cinema, moda e música para a produção do videojogo.',
    ru: 'Кодзима работает как режиссёр, который никогда не забывает о контроллере. Актёры, музыка, визуальный стиль, монтаж и игровые системы становятся частями единой постановки. Его фотокампании показывают этот метод: автор не только ставит подпись, но создаёт вокруг произведения узнаваемый мир и приглашает в игру кино, моду и музыку.',
    uk: 'Кодзіма працює як режисер, який ніколи не забуває про контролер. Актори, музика, візуальний стиль, монтаж та ігрові системи стають частинами єдиної постановки. Його фотокампанії показують цей метод: автор не лише ставить підпис, а створює навколо твору впізнаваний світ і запрошує до гри кіно, моду та музику.',
    ja: '小島はコントローラーを忘れない映画監督のように仕事をする。俳優、音楽、ビジュアル、編集、ゲームシステムを同じ演出の一部として扱う。写真キャンペーンはその方法を可視化する。作品に署名するだけでなく、その周囲に識別できる世界を築き、映画、ファッション、音楽をゲーム制作へ招き入れる。',
    ko: '고지마는 컨트롤러를 잊지 않는 영화감독처럼 일한다. 배우와 음악, 시각 디자인, 편집, 게임 시스템을 하나의 연출로 다룬다. 사진 캠페인은 이 방법을 드러낸다. 작품에 서명하는 데 그치지 않고 주변에 알아볼 수 있는 세계를 만들며 영화와 패션, 음악을 게임 제작으로 불러들인다.',
    zh: '小岛像一位从不忘记控制器的导演那样工作。演员、音乐、视觉设计、剪辑与游戏系统都属于同一场调度。他的摄影企划让这种方法可见：作者不只为作品署名，也在它周围建立鲜明世界，将电影、时尚与音乐带入游戏制作。',
    hi: 'कोजिमा ऐसे निर्देशक की तरह काम करते हैं जो कंट्रोलर को कभी नहीं भूलता। अभिनेता, संगीत, दृश्य-रचना, संपादन और गेम प्रणालियाँ एक ही निर्देशन का हिस्सा बनती हैं। उनकी फ़ोटोग्राफ़िक मुहिम इस विधि को दिखाती हैं: वे सिर्फ़ हस्ताक्षर नहीं करते, एक पहचाने जाने योग्य संसार बनाते और सिनेमा, फ़ैशन व संगीत को गेम निर्माण में बुलाते हैं।'
  }
};

const studio = {
  subject: 'Kojima Productions',
  kicker: 'KOJIMA PRODUCTIONS · 2015',
  image_role: 'company',
  image_file: 'kojima-productions.png',
  title: {
    it: 'Ricominciare con il proprio nome', en: 'Starting again under his own name', de: 'Unter dem eigenen Namen neu beginnen',
    es: 'Volver a empezar con su propio nombre', fr: 'Recommencer sous son propre nom', pt: 'Recomeçar com o próprio nome',
    ru: 'Начать заново под своим именем', uk: 'Почати знову під власним ім’ям', ja: '自分の名前で、もう一度始める',
    ko: '자신의 이름으로 다시 시작하다', zh: '以自己的名字重新开始', hi: 'अपने नाम से फिर शुरुआत'
  },
  body: {
    it: 'Nel dicembre 2015, dopo la separazione da Konami, Kojima fonda a Tokyo uno studio indipendente. Sony annuncia subito un accordo per il primo progetto; pochi mesi dopo nasce Ludens, figura-manifesto di un gruppo che si definisce “quelli che giocano”. Ricominciare significa trasformare trent’anni di relazioni in una nuova casa creativa, capace di arrivare a Death Stranding senza rinunciare a un’identità autoriale.',
    en: 'In December 2015, after separating from Konami, Kojima established an independent studio in Tokyo. Sony immediately announced an agreement for its first project; a few months later came Ludens, the emblem of a team calling itself “those who play.” Starting again meant turning thirty years of relationships into a new creative home, one that could reach Death Stranding without surrendering an authorial identity.',
    de: 'Im Dezember 2015 gründete Kojima nach der Trennung von Konami ein unabhängiges Studio in Tokio. Sony kündigte sofort eine Vereinbarung für das erste Projekt an; wenige Monate später erschien Ludens als Symbol eines Teams, das sich „die Spielenden“ nennt. Der Neustart verwandelte dreißig Jahre Beziehungen in eine neue kreative Heimat auf dem Weg zu Death Stranding.',
    es: 'En diciembre de 2015, tras separarse de Konami, Kojima fundó un estudio independiente en Tokio. Sony anunció de inmediato un acuerdo para su primer proyecto; pocos meses después nació Ludens, emblema de un equipo que se define como “los que juegan”. Recomenzar significó convertir treinta años de relaciones en un nuevo hogar creativo capaz de llegar a Death Stranding.',
    fr: 'En décembre 2015, après sa séparation avec Konami, Kojima fonde un studio indépendant à Tokyo. Sony annonce aussitôt un accord pour son premier projet ; quelques mois plus tard apparaît Ludens, emblème d’une équipe qui se définit comme « ceux qui jouent ». Recommencer transforme trente ans de relations en une nouvelle maison créative, jusqu’à Death Stranding.',
    pt: 'Em dezembro de 2015, após separar-se da Konami, Kojima fundou um estúdio independente em Tóquio. A Sony anunciou de imediato um acordo para o primeiro projeto; poucos meses depois surgiu Ludens, emblema de uma equipa que se define como “aqueles que jogam”. Recomeçar transformou trinta anos de relações numa nova casa criativa a caminho de Death Stranding.',
    ru: 'В декабре 2015 года, после расставания с Konami, Кодзима основал независимую студию в Токио. Sony сразу объявила соглашение о первом проекте; через несколько месяцев появился Люденс — символ команды, называющей себя «теми, кто играет». Начать заново означало превратить тридцать лет связей в новый творческий дом, который привёл к Death Stranding.',
    uk: 'У грудні 2015 року, після розриву з Konami, Кодзіма заснував незалежну студію в Токіо. Sony одразу оголосила угоду щодо першого проєкту; за кілька місяців з’явився Люденс — символ команди, що називає себе «тими, хто грає». Почати знову означало перетворити тридцять років зв’язків на новий творчий дім, який привів до Death Stranding.',
    ja: '2015年12月、コナミを離れた小島は東京に独立スタジオを設立した。ソニーはすぐに最初の企画での提携を発表し、数か月後には「遊ぶ人」を掲げるチームの象徴ルーデンスが姿を現す。再出発とは、30年かけて築いたつながりを新しい創作の家へ変え、作家性を失わずに『DEATH STRANDING』へ進むことだった。',
    ko: '2015년 12월 코나미와 결별한 고지마는 도쿄에 독립 스튜디오를 세웠다. 소니는 곧 첫 프로젝트 협업을 발표했고, 몇 달 뒤 “노는 사람들”을 표방한 팀의 상징 루덴스가 등장했다. 다시 시작한다는 것은 30년의 관계를 새로운 창작의 집으로 바꾸고 작가성을 지킨 채 데스 스트랜딩으로 나아가는 일이었다.',
    zh: '2015年12月，与科乐美分开后，小岛在东京成立独立工作室。索尼随即宣布合作首个项目；几个月后，象征“游戏之人”的Ludens诞生。重新开始意味着把三十年的关系转化为新的创作之家，在保持作者身份的同时走向《死亡搁浅》。',
    hi: 'दिसंबर 2015 में Konami से अलग होने के बाद कोजिमा ने टोक्यो में स्वतंत्र स्टूडियो बनाया। Sony ने तुरंत पहले प्रोजेक्ट के समझौते की घोषणा की; कुछ महीनों बाद “खेलने वालों” की टीम का प्रतीक Ludens सामने आया। फिर शुरुआत का अर्थ था तीस वर्षों के रिश्तों को नए रचनात्मक घर में बदलना, जो अपनी लेखक पहचान रखते हुए Death Stranding तक पहुँचा।'
  }
};
studio.kicker = Object.fromEntries(Object.keys(studio.title).map(lang=>[lang,'KOJIMA PRODUCTIONS · 2015']));

youth.kicker = Object.fromEntries(Object.keys(youth.title).map(lang=>[lang,'HIDEO KOJIMA']));
method.kicker = Object.fromEntries(Object.keys(method.title).map(lang=>[lang,'HIDEO KOJIMA']));
// The final narrative ordering and the anti-photo-caption rule are applied by
// revise-creator-narration.py after this asset migration.
theme.chapters = [youth, metalGearSolid, method, studio, deathStranding];
theme.sources = [...new Set([
  ...(theme.sources ?? []),
  'https://www.kojimaproductions.jp/en/company',
  'https://www.konami.com/mg/history/jp/ja/'
])];

const supplied = (file, subject, kind) => ({
  file,
  url: `local://${file}`,
  sourceUrl: 'User supplied to the Playhub project',
  subject,
  kind,
  provider: 'User supplied',
  credit: 'Image supplied by the Playhub project owner',
  rights: 'Image supplied by the Playhub project owner'
});
const metalGearImage = (images.metalgear ?? []).find((item) => item.file === 'metalgear-0.jpg');
if (!metalGearImage) throw new Error('Metal Gear Solid image not found');
images.kojima = [
  supplied('kojima-intro.png', 'Hideo Kojima', 'portrait'),
  supplied('kojima-youth.png', 'Hideo Kojima', 'portrait'),
  {...metalGearImage, kind: 'screenshot'},
  supplied('kojima-cinematic.png', 'Hideo Kojima', 'portrait'),
  supplied('death-stranding-game.png', 'Death Stranding', 'screenshot'),
  supplied('kojima-stage.png', 'Hideo Kojima', 'portrait'),
  supplied('kojima-productions.png', 'Kojima Productions', 'company')
];

fs.writeFileSync(editorialPath, `${JSON.stringify(editorial, null, 1)}\n`);
fs.writeFileSync(imagesPath, `${JSON.stringify(images, null, 1)}\n`);
console.log(`Updated Kojima: ${theme.chapters.length} chapters, ${images.kojima.length} curated images.`);

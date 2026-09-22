import { readFile, writeFile } from 'node:fs/promises';

const file = new URL('./calendar-replacements.json', import.meta.url);
const proposal = JSON.parse(await readFile(file, 'utf8'));
const languages = ['de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi'];
const headings = {
  '01-15': ['Supporti e immaginazione', 'Media and imagination', 'The CD-ROM'],
  '01-17': ['Architetture da attraversare', 'Architecture to walk through', 'Brutalism in virtual worlds'],
  '01-24': ["Giornata internazionale dell'educazione", 'International Day of Education', 'The Logo turtle'],
  '04-02': ['Leggere prima di giocare', 'Reading before playing', 'The manual in the box'],
  '05-01': ['La fisica del gioco', 'The physics of play', 'Pinball'],
  '05-02': ['Costruire per raccontare', 'Building to tell stories', 'LEGO video games'],
  '05-18': ['Il segno e il paesaggio', 'The mark and the landscape', 'The brush and empty space'],
  '05-19': ['Il gioco diventa costume', 'Games become costumes', 'Cosplay'],
  '05-22': ['Mondi in miniatura', 'Miniature worlds', 'The diorama'],
  '05-31': ['Le tracce di chi ha giocato', 'Traces left by players', 'The archaeology of video games'],
  '07-31': ['Viaggio nella città elettrica', 'A journey through Electric Town', 'Akihabara'],
  '09-02': ['Immaginare il futuro', 'Imagining the future', 'The cyberpunk imagination'],
  '10-10': ["Il suono sotto la superficie", 'Sound beneath the surface', 'Listening underwater'],
  '10-29': ["I simboli dell'appartenenza", 'Symbols of belonging', 'Flags of imaginary worlds'],
};

const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
async function translate(lines, language) {
  const query = new URLSearchParams({client:'gtx', sl:'en', tl:language === 'zh' ? 'zh-TW' : language, dt:'t', q:lines.join('\n')});
  for (let attempt=0; attempt<4; attempt++) {
    try {
      const response = await fetch(`https://translate.googleapis.com/translate_a/single?${query}`, {signal:AbortSignal.timeout(30000)});
      if (!response.ok) throw new Error(`translation HTTP ${response.status}`);
      const data=await response.json();
      const result=data[0].map(segment=>segment[0]??'').join('').split(/\r?\n/).map(line=>line.trim()).filter(Boolean);
      if (result.length !== lines.length) throw new Error(`line count ${result.length}, expected ${lines.length}`);
      return result;
    } catch(error) {
      if (attempt===3) throw error;
      await pause(1500*(attempt+1));
    }
  }
}

for (const record of proposal.replacements) {
  const [it, en, subject] = headings[record.date];
  record.occasion_headline ??= {it,en};
  record.subject_localized ??= {it:record.subject,en:subject};
  const fields = [record.title, record.intro, record.occasion_headline, record.subject_localized,
    ...record.chapters.flatMap(chapter=>[chapter.title,chapter.body,chapter.kicker]),
    ...(record.occasion ? [record.occasion] : [])];
  for (const language of languages) {
    if (fields.every(field=>typeof field[language]==='string' && field[language].trim())) continue;
    const translated=await translate(fields.map(field=>field.en),language);
    fields.forEach((field,index)=>{ field[language]=translated[index]; });
    await writeFile(file,JSON.stringify(proposal,null,2)+'\n');
    console.log(`${record.date} ${language}: ${fields.length} fields`);
    await pause(250);
  }
}
// Chapter subjects are labels, not prose: keep their terminology explicit.
const chapterSubjects = {
  '01-15': ['CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM','CD-ROM'],
  '01-17': ['Architettura brutalista','Brutalist architecture','Brutalistische Architektur','Arquitectura brutalista','Architecture brutaliste','Arquitetura brutalista','Бруталистская архитектура','Бруталістська архітектура','ブルータリズム建築','브루탈리즘 건축','粗獷主義建築','ब्रूटलिस्ट वास्तुकला'],
  '01-24': ['Logo','Logo','Logo','Logo','Logo','Logo','Logo','Logo','Logo','Logo','Logo','Logo'],
  '04-02': ['Manuali dei videogiochi','Video game manuals','Videospielhandbücher','Manuales de videojuegos','Manuels de jeux vidéo','Manuais de jogos','Руководства к видеоиграм','Посібники до відеоігор','ゲームの説明書','게임 설명서','遊戲說明書','वीडियो गेम की निर्देश-पुस्तिकाएँ'],
  '05-01': ['Flipper','Pinball','Flipper','Pinball','Flipper','Pinball','Пинбол','Пінбол','ピンボール','핀볼','彈珠台','पिनबॉल'],
  '05-02': ['Videogiochi LEGO','LEGO video games','LEGO-Videospiele','Videojuegos LEGO','Jeux vidéo LEGO','Videogames LEGO','Видеоигры LEGO','Відеоігри LEGO','LEGOのゲーム','LEGO 비디오 게임','LEGO 電子遊戲','LEGO वीडियो गेम'],
  '05-18': ['Pittura a inchiostro','Ink wash painting','Tuschemalerei','Pintura a tinta','Peinture à l’encre','Pintura a tinta','Живопись тушью','Живопис тушшю','水墨画','수묵화','水墨畫','स्याही से चित्रकला'],
  '05-19': ['Cosplay videoludico','Video game cosplay','Videospiel-Cosplay','Cosplay de videojuegos','Cosplay de jeux vidéo','Cosplay de videogames','Косплей по видеоиграм','Косплей за відеоіграми','ゲームのコスプレ','게임 코스프레','遊戲角色扮演','वीडियो गेम कॉस्प्ले'],
  '05-22': ['Diorami','Dioramas','Dioramen','Dioramas','Dioramas','Dioramas','Диорамы','Діорами','ジオラマ','디오라마','立體透視模型','लघु दृश्य-प्रतिरूप'],
  '05-31': ['Archeologia dei videogiochi','Archaeogaming','Videospielarchäologie','Arqueología de los videojuegos','Archéologie des jeux vidéo','Arqueologia dos videogames','Археология видеоигр','Археологія відеоігор','ゲーム考古学','게임 고고학','遊戲考古學','वीडियो गेम पुरातत्त्व'],
  '07-31': ['Akihabara','Akihabara','Akihabara','Akihabara','Akihabara','Akihabara','Акихабара','Акіхабара','秋葉原','아키하바라','秋葉原','अकिहाबारा'],
  '09-02': ['Cyberpunk','Cyberpunk','Cyberpunk','Ciberpunk','Cyberpunk','Cyberpunk','Киберпанк','Кіберпанк','サイバーパンク','사이버펑크','賽博龐克','साइबरपंक'],
  '10-10': ['Acustica subacquea','Underwater acoustics','Unterwasserakustik','Acústica subacuática','Acoustique sous-marine','Acústica subaquática','Подводная акустика','Підводна акустика','水中音響学','수중 음향학','水下聲學','पानी के भीतर ध्वनिकी'],
  '10-29': ['Araldica e mondi fantastici','Heraldry and fantasy worlds','Heraldik und Fantasiewelten','Heráldica y mundos fantásticos','Héraldique et mondes fantastiques','Heráldica e mundos fantásticos','Геральдика и фантастические миры','Геральдика та фантастичні світи','紋章と空想世界','문장학과 판타지 세계','紋章與幻想世界','कुलचिह्न और काल्पनिक संसार'],
};
const locales = ['it','en',...languages];
for(const record of proposal.replacements) {
  record.chapters[0].subject_localized=Object.fromEntries(locales.map((language,index)=>[language,chapterSubjects[record.date][index]]));
}
const byDate=Object.fromEntries(proposal.replacements.map(record=>[record.date,record]));
const editedTitles = {
  '01-15': ['Eine Scheibe für große Träume','Un disco para soñar a lo grande','Un disque pour voir plus grand','Um disco para sonhar em grande','Диск для больших замыслов','Диск для великих задумів','大きな夢を収める一枚のディスク','더 큰 꿈을 담는 한 장의 디스크','一張容納宏大想像的光碟','बड़ी कल्पनाओं के लिए एक डिस्क'],
  '01-17': ['Beton kann einschüchtern','El hormigón sabe imponerse','Le béton sait intimider','O concreto sabe intimidar','Бетон умеет внушать трепет','Бетон уміє вселяти трепет','コンクリートが放つ威圧感','콘크리트가 주는 위압감','混凝土的威懾力','कंक्रीट का रौब'],
  '01-24': ['Eine Schildkröte lehrt das Denken','Una tortuga enseña a pensar','Une tortue apprend à penser','Uma tartaruga ensina a pensar','Черепаха учит мыслить','Черепаха вчить мислити','考えることを教えるカメ','생각하는 법을 가르치는 거북이','一隻教人思考的烏龜','सोचना सिखाने वाला कछुआ'],
  '04-02': ['Das Spiel beginnt auf Papier','La partida empieza en el papel','La partie commence sur le papier','A partida começa no papel','Игра начинается на бумаге','Гра починається на папері','遊びは紙の上から始まる','게임은 종이 위에서 시작된다','遊戲從紙上開始','खेल कागज़ पर शुरू होता है'],
  '05-01': ['Die Physik wartet nicht, bis du dran bist','La física no espera tu turno','La physique n’attend pas ton tour','A física não espera a sua vez','Физика не ждёт твоего хода','Фізика не чекає твого ходу','物理法則は順番を待ってくれない','물리 법칙은 차례를 기다려 주지 않는다','物理法則不會等你回合','भौतिकी आपकी बारी का इंतज़ार नहीं करती'],
  '05-02': ['Eine Geschichte zerlegen und damit spielen','Desmontar una historia para jugar con ella','Démonter une histoire pour jouer avec','Desmontar uma história para brincar com ela','Разобрать историю, чтобы с ней поиграть','Розібрати історію, щоб із нею пограти','物語を分解して遊ぶ','이야기를 분해해서 놀다','拆開故事，再拿來玩','कहानी को खोलकर उसके साथ खेलना'],
  '05-18': ['Raum für die Fantasie lassen','Dejar espacio a la imaginación','Laisser place à l’imagination','Deixar espaço para a imaginação','Оставить место воображению','Залишити місце уяві','想像の余白を残す','상상할 여백을 남기다','為想像留下空白','कल्पना के लिए जगह छोड़ना'],
  '05-19': ['Eine Figur tritt aus dem Bildschirm','Un personaje sale de la pantalla','Un personnage sort de l’écran','Um personagem sai da tela','Персонаж выходит из экрана','Персонаж виходить з екрана','画面から歩み出すキャラクター','화면 밖으로 걸어 나온 캐릭터','角色走出螢幕','पर्दे से बाहर आता एक किरदार'],
  '05-22': ['Eine Welt, die sich ganz überblicken lässt','Un mundo lo bastante pequeño para verlo entero','Un monde assez petit pour l’embrasser du regard','Um mundo pequeno o bastante para vê-lo por inteiro','Мир, который можно охватить взглядом','Світ, який можна охопити поглядом','全体を見渡せる小さな世界','한눈에 담을 수 있는 작은 세계','一個能盡收眼底的小世界','इतनी छोटी दुनिया कि पूरी नज़र आ जाए'],
  '05-31': ['In einer geschaffenen Welt graben','Excavar un mundo construido','Fouiller un monde construit','Escavar um mundo construído','Раскопки в созданном мире','Розкопки у створеному світі','作られた世界を発掘する','만들어진 세계를 발굴하다','挖掘一個被建構的世界','रची हुई दुनिया की खुदाई'],
  '07-31': ['Ein Viertel liest man von unten nach oben','Un barrio se lee en vertical','Un quartier se lit à la verticale','Um bairro se lê na vertical','Район, который читают по вертикали','Район, який читають по вертикалі','縦に読み解く街','위아래로 읽는 거리','垂直閱讀一個街區','ऊपर की ओर पढ़ा जाने वाला इलाका'],
  '09-02': ['Die Zukunft hat bereits Risse','El futuro ya tiene grietas','Le futur a déjà ses fissures','O futuro já tem rachaduras','Будущее уже дало трещину','Майбутнє вже дало тріщину','未来にはもう亀裂がある','미래에는 이미 균열이 있다','未來早已有了裂縫','भविष्य में दरारें अभी से हैं'],
  '10-10': ['Ein Ozean entsteht auch fürs Ohr','Un océano también se construye de oído','Un océan se construit aussi à l’oreille','Um oceano também se constrói para os ouvidos','Океан создают и для слуха','Океан створюють і для слуху','海は音からも生まれる','바다는 소리로도 만들어진다','海洋也由聲音構築','समंदर ध्वनियों से भी रचा जाता है'],
  '10-29': ['Ein Königreich aus der Ferne erkennen','Reconocer un reino desde lejos','Reconnaître un royaume de loin','Reconhecer um reino de longe','Узнать королевство издалека','Впізнати королівство здалеку','遠くから王国を見分ける','멀리서 왕국을 알아보다','從遠處辨認一個王國','दूर से ही राज्य को पहचानना'],
};
const editedChapterTitles = {
  '01-15': ['Die Pause wird Teil der Inszenierung','La pausa entra en la puesta en escena','La pause entre dans la mise en scène','A pausa entra na encenação','Пауза становится частью режиссуры','Пауза стає частиною режисури','待ち時間も演出になる','기다림도 연출의 일부가 된다','停頓也成為演出的一部分','ठहराव भी निर्देशन का हिस्सा बनता है'],
  '01-17': ['Klein vor dem Tor','Pequeños ante la puerta','Tout petits devant la porte','Pequenos diante da porta','Маленькими перед дверью','Маленькими перед дверима','扉の前で小さくなる','문 앞에서 작아지다','在門前顯得渺小','दरवाज़े के सामने छोटे पड़ते हम'],
  '01-24': ['Ein Fehler hinterlässt eine Spur','Un error deja huella','Une erreur laisse une trace','Um erro deixa um rastro','Ошибка оставляет след','Помилка залишає слід','間違いが跡を残す','실수는 흔적을 남긴다','錯誤會留下痕跡','गलती अपना निशान छोड़ती है'],
  '04-02': ['Ein Heft, das offen liegen bleibt','Un objeto que se deja abierto','Un livret qu’on laisse ouvert','Um livreto para deixar aberto','Книжка, которую оставляют раскрытой','Книжечка, яку залишають розгорнутою','開いたままにしておく一冊','펼쳐 둔 채로 보는 책자','一本攤開放在身旁的手冊','पास में खुली रखी एक पुस्तिका'],
  '05-01': ['Eine Maschine in Bewegung lesen','Leer una máquina en movimiento','Lire une machine en mouvement','Ler uma máquina em movimento','Читать машину в движении','Читати машину в русі','動く機械を読み解く','움직이는 기계를 읽다','讀懂運動中的機台','चलती मशीन को पढ़ना'],
  '05-02': ['Die Bewegung bleibt vertraut','El gesto sigue siendo familiar','Le geste reste familier','O gesto continua familiar','Движение остаётся знакомым','Рух залишається знайомим','動作は変わらずなじみ深い','동작은 여전히 익숙하다','動作依然熟悉','हरकत अब भी जानी-पहचानी है'],
  '05-18': ['Eine Landschaft, die nicht alles verrät','Un paisaje que no lo cuenta todo','Un paysage qui ne dit pas tout','Uma paisagem que não conta tudo','Пейзаж, который не всё объясняет','Краєвид, який не все пояснює','すべてを語らない風景','모든 것을 말하지 않는 풍경','不把一切說盡的風景','जो सब कुछ न कहे, ऐसा दृश्य'],
  '05-19': ['Was auf dem Foto unsichtbar bleibt','Lo que la fotografía no muestra','Ce que la photographie ne montre pas','O que a fotografia não mostra','То, чего не видно на фотографии','Те, чого не видно на світлині','写真には写らないもの','사진에 보이지 않는 것','照片裡看不見的部分','तस्वीर में जो दिखाई नहीं देता'],
  '05-22': ['Der Rand gehört zur Geschichte','El borde forma parte del relato','Le bord fait partie du récit','A borda faz parte da história','Край становится частью истории','Край стає частиною історії','縁も物語の一部になる','가장자리도 이야기의 일부다','邊界也是故事的一部分','किनारा भी कहानी का हिस्सा है'],
  '05-31': ['Ein Fund braucht seinen Zusammenhang','Un objeto necesita su contexto','Un objet a besoin de son contexte','Um objeto precisa de seu contexto','Предмету нужен контекст','Предмету потрібен контекст','物には文脈が必要だ','사물에는 맥락이 필요하다','物件需要脈絡','वस्तु को उसका संदर्भ चाहिए'],
  '07-31': ['Die Geografie einer Leidenschaft','La geografía de una pasión','La géographie d’une passion','A geografia de uma paixão','География увлечения','Географія захоплення','情熱が描く地理','열정이 그리는 지리','熱情的地理','जुनून का भूगोल'],
  '09-02': ['Wer bestimmt den Zugang?','Quién controla el acceso','Qui contrôle l’accès','Quem controla o acesso','Кто управляет доступом','Хто керує доступом','誰がアクセスを支配するのか','누가 접근을 통제하는가','誰掌控進入的權利','प्रवेश पर किसका नियंत्रण है'],
  '10-10': ['Eine Präsenz außerhalb des Bildes','Una presencia fuera de plano','Une présence hors champ','Uma presença fora de quadro','Присутствие за кадром','Присутність поза кадром','画面の外にある気配','화면 밖의 존재감','畫面之外的存在','फ़्रेम के बाहर की मौजूदगी'],
  '10-29': ['Ein Zeichen muss aus der Ferne lesbar bleiben','Un símbolo debe resistir la distancia','Un symbole doit rester lisible de loin','Um símbolo precisa resistir à distância','Символ должен быть различим издалека','Символ має бути впізнаваним здалеку','遠くからでも伝わるしるし','멀리서도 알아볼 수 있는 상징','遠看仍能辨識的符號','दूर से भी पहचाना जाने वाला चिह्न'],
};
for(const [date,values] of Object.entries(editedTitles)) languages.forEach((language,index)=>{byDate[date].title[language]=values[index];});
for(const [date,values] of Object.entries(editedChapterTitles)) languages.forEach((language,index)=>{byDate[date].chapters[0].title[language]=values[index];});
Object.assign(byDate['01-15'].chapters[0].kicker, {fr:'LE SUPPORT OPTIQUE',ru:'ОПТИЧЕСКИЙ НОСИТЕЛЬ',uk:'ОПТИЧНИЙ НОСІЙ'});
Object.assign(byDate['01-17'].title, {zh:'混凝土的威懾力',ja:'コンクリートが放つ威圧感',ko:'콘크리트가 주는 위압감'});
Object.assign(byDate['01-17'].chapters[0].kicker, {fr:'ÉCHELLE ET MATIÈRE'});
Object.assign(byDate['01-24'].chapters[0].kicker, {fr:'APPRENDRE EN CONSTRUISANT'});
byDate['01-24'].occasion_headline.ja='教育の国際デー';
byDate['01-24'].occasion.ja='教育の国際デー';
byDate['05-18'].chapters[0].body.uk='Співвідношення чітких контурів і ледь намічених ділянок спрямовує увагу. Такий погляд допомагає розглядати ігровий краєвид: і деталі, і пропуски мають своє призначення. Порівняння стосується способів бачення, а не автоматичної спорідненості; конкретний вплив слід підтверджувати свідченнями з роботи авторів.';
proposal.note = 'Feature slots are editorial appointments, not invented anniversaries. Images must be acquired and visually checked before publication. Original Italian and English copy is preserved; all 12 locale fields are present. See translation_review for review limits.';
proposal.translation_review={method:'Google Translate from original English with targeted terminology corrections; original Italian and English preserved',
  locales:['it','en',...languages],review_status:'structural validation complete; native-language editorial review still required'};
await writeFile(file,JSON.stringify(proposal,null,2)+'\n');

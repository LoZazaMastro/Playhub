"""Apply narrative corrections requested after the live creator review.

The article must tell a career or a work.  Images illustrate that story; prose must
never describe the selected photograph.  This script keeps the generated catalog and
the reusable creator source in sync.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "quick_settings" / "history_editorial.json"
MIYAMOTO_SOURCE = ROOT / "tools" / "miyamoto-content.json"
DAYS = ROOT / "quick_settings" / "history_days.json"
IMAGES = ROOT / "quick_settings" / "history_images.json"
METAL_GEAR_CONTENT = ROOT / "tools" / "kojima-metalgear-content.json"


def localized(**values):
    required = {"it", "en", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"}
    if set(values) != required:
        raise ValueError(f"missing translations: {sorted(required - set(values))}")
    return values


konami = {
    "subject": "Hideo Kojima",
    "kicker": localized(**{language: "HIDEO KOJIMA · KONAMI · 1986" for language in
                            ("it", "en", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi")}),
    "image_role": "portrait",
    "image_file": "kojima-cinematic.png",
    "title": localized(
        it="Entrare dalla porta meno attesa", en="Entering through an unexpected door",
        de="Durch eine unerwartete Tür", es="Entrar por la puerta menos esperada",
        fr="Entrer par une porte inattendue", pt="Entrar pela porta menos esperada",
        ru="Войти через неожиданную дверь", uk="Увійти через несподівані двері",
        ja="思いがけない扉から入る", ko="뜻밖의 문으로 들어가다", zh="从意想不到的门进入",
        hi="एक अनपेक्षित दरवाज़े से प्रवेश"),
    "body": localized(
        it="Nel 1986 Kojima entra in Konami come designer e planner, mentre il settore considera ancora rischioso raccontare storie lunghe attraverso un videogioco. I primi progetti non procedono senza ostacoli: deve imparare a tradurre ambizioni cinematografiche in regole, memoria e tempi di una macchina. L'anno seguente riceve una sfida concreta, costruire un gioco di guerra per MSX2. È il passaggio che collega il giovane aspirante regista alla nascita di Metal Gear.",
        en="In 1986 Kojima joined Konami as a designer and planner, when long-form storytelling in games was still considered risky. His first projects did not progress without obstacles: he had to translate cinematic ambitions into rules, memory and machine timing. The following year brought a concrete challenge, to build a war game for the MSX2. That passage connects the young would-be filmmaker to the birth of Metal Gear.",
        de="1986 kam Kojima als Designer und Planer zu Konami, als lange Erzählungen in Spielen noch als riskant galten. Seine ersten Projekte verliefen nicht reibungslos: Filmische Ambitionen mussten zu Regeln, Speicher und Maschinentakt werden. Im folgenden Jahr erhielt er die konkrete Aufgabe, ein Kriegsspiel für den MSX2 zu entwickeln. So führt der Weg des jungen Filmbegeisterten zu Metal Gear.",
        es="En 1986 Kojima entró en Konami como diseñador y planificador, cuando contar historias largas en un videojuego aún parecía arriesgado. Sus primeros proyectos encontraron obstáculos: debía traducir ambiciones cinematográficas en reglas, memoria y tiempos de máquina. Al año siguiente recibió un reto concreto, crear un juego bélico para MSX2. Ese paso une al joven aspirante a cineasta con el nacimiento de Metal Gear.",
        fr="En 1986, Kojima rejoint Konami comme designer et planificateur, alors que les récits longs dans le jeu vidéo paraissent encore risqués. Ses premiers projets rencontrent des obstacles : il doit traduire ses ambitions de cinéma en règles, mémoire et rythme de machine. L'année suivante arrive un défi concret, concevoir un jeu de guerre pour MSX2. Ce passage relie le jeune cinéaste rêvé à la naissance de Metal Gear.",
        pt="Em 1986, Kojima entrou na Konami como designer e planeador, quando narrativas longas nos videojogos ainda pareciam arriscadas. Os primeiros projetos enfrentaram obstáculos: era preciso traduzir ambições cinematográficas em regras, memória e tempos de máquina. No ano seguinte surgiu um desafio concreto, criar um jogo de guerra para MSX2. Esse passo liga o jovem aspirante a cineasta ao nascimento de Metal Gear.",
        ru="В 1986 году Кодзима пришёл в Konami дизайнером и планировщиком, когда длинные игровые истории ещё считались рискованными. Первые проекты шли с трудом: кинематографические амбиции пришлось переводить в правила, память и ритм машины. Через год он получил конкретную задачу — создать военную игру для MSX2. Так путь молодого будущего режиссёра привёл к рождению Metal Gear.",
        uk="1986 року Кодзіма прийшов до Konami дизайнером і планувальником, коли довгі ігрові історії ще вважали ризикованими. Перші проєкти просувалися непросто: кінематографічні амбіції довелося переводити у правила, пам'ять і ритм машини. Наступного року він отримав конкретне завдання — створити військову гру для MSX2. Так шлях молодого майбутнього режисера привів до народження Metal Gear.",
        ja="1986年、小島はデザイナー兼プランナーとしてコナミに入社した。ゲームで長い物語を語ることがまだ危険視された時代で、最初の企画は順調ではない。映画への志をルール、メモリ、機械の時間へ翻訳する必要があった。翌年、MSX2向けの戦争ゲームという具体的な課題を得る。若き映画志望者と『メタルギア』の誕生を結ぶ転機だった。",
        ko="1986년 고지마는 디자이너이자 기획자로 코나미에 입사했다. 게임으로 긴 이야기를 전하는 일이 여전히 위험하게 여겨지던 때였다. 첫 프로젝트들은 순탄하지 않았고 영화적 야심을 규칙과 메모리, 기계의 시간으로 옮겨야 했다. 이듬해 MSX2용 전쟁 게임이라는 구체적인 과제를 맡으며 젊은 영화 지망생의 길은 메탈 기어의 탄생으로 이어졌다.",
        zh="1986年，小岛以设计师和企划身份进入科乐美，当时用游戏讲述长篇故事仍被视为冒险。他最初的项目并不顺利，必须把电影抱负转译为规则、内存与机器节奏。次年，他面对一项具体任务：为MSX2制作战争游戏。这一步把年轻的电影志愿者带向《合金装备》的诞生。",
        hi="1986 में कोजिमा डिज़ाइनर और प्लानर के रूप में Konami आए, जब गेम में लंबी कहानी कहना जोखिम माना जाता था। शुरुआती परियोजनाएँ आसानी से नहीं बढ़ीं; सिनेमाई महत्वाकांक्षा को नियम, मेमरी और मशीन के समय में बदलना पड़ा। अगले वर्ष उन्हें MSX2 के लिए युद्ध गेम बनाने की ठोस चुनौती मिली। यही मोड़ युवा फ़िल्मकार बनने की इच्छा को Metal Gear के जन्म से जोड़ता है।")
}

death_title = localized(
    it="Unire le persone", en="Connecting people", de="Menschen verbinden",
    es="Conectar a las personas", fr="Relier les personnes", pt="Ligar as pessoas",
    ru="Соединять людей", uk="Поєднувати людей", ja="人と人をつなぐ", ko="사람을 잇다",
    zh="连接人与人", hi="लोगों को जोड़ना")
death_body = localized(
    it="Death Stranding nasce dopo la rifondazione di Kojima Productions e mette il legame al centro delle sue regole. Sam attraversa un'America spezzata per riconnettere comunità isolate; scale, corde e sentieri lasciati dagli altri trasformano la fatica individuale in collaborazione asincrona. Kojima usa il viaggio per chiedere che cosa significhi avvicinarsi senza occupare lo stesso spazio. Consegnare un carico diventa così un gesto di fiducia verso persone che forse non incontreremo mai.",
    en="Created after Kojima Productions was rebuilt, Death Stranding places connection at the centre of its rules. Sam crosses a fractured America to reconnect isolated communities; ladders, ropes and paths left by other players turn individual effort into asynchronous cooperation. Kojima uses the journey to ask what drawing closer can mean without sharing the same space. Delivering cargo becomes an act of trust toward people we may never meet.",
    de="Death Stranding entstand nach dem Neubeginn von Kojima Productions und stellt Verbindung ins Zentrum seiner Regeln. Sam durchquert ein zerbrochenes Amerika, um isolierte Gemeinschaften zu verbinden; Leitern, Seile und Wege anderer machen aus persönlicher Mühe asynchrone Zusammenarbeit. Die Reise fragt, wie Nähe ohne gemeinsamen Ort entstehen kann. Eine Lieferung wird zum Vertrauensbeweis gegenüber Menschen, denen wir vielleicht nie begegnen.",
    es="Death Stranding nace tras la refundación de Kojima Productions y sitúa el vínculo en el centro de sus reglas. Sam cruza una América rota para reconectar comunidades aisladas; escaleras, cuerdas y caminos de otros jugadores convierten el esfuerzo individual en cooperación asíncrona. El viaje pregunta qué significa acercarse sin compartir espacio. Entregar una carga se vuelve un gesto de confianza hacia personas que quizá nunca conozcamos.",
    fr="Né après la refondation de Kojima Productions, Death Stranding place le lien au centre de ses règles. Sam traverse une Amérique brisée pour reconnecter des communautés isolées ; échelles, cordes et chemins laissés par d'autres transforment l'effort individuel en coopération asynchrone. Le voyage demande comment se rapprocher sans partager le même espace. Livrer une cargaison devient un geste de confiance envers des personnes que nous ne rencontrerons peut-être jamais.",
    pt="Death Stranding nasce após a refundação da Kojima Productions e põe a ligação no centro das regras. Sam atravessa uma América fragmentada para reconectar comunidades isoladas; escadas, cordas e caminhos deixados por outros transformam esforço individual em cooperação assíncrona. A viagem pergunta o que significa aproximar-se sem ocupar o mesmo espaço. Entregar uma carga torna-se um gesto de confiança em pessoas que talvez nunca encontremos.",
    ru="Death Stranding появился после перезапуска Kojima Productions и поставил связь в центр правил. Сэм пересекает расколотую Америку, соединяя изолированные сообщества; лестницы, канаты и тропы других игроков превращают личное усилие в асинхронное сотрудничество. Путешествие спрашивает, что значит стать ближе, не находясь рядом. Доставка становится жестом доверия людям, которых мы можем никогда не встретить.",
    uk="Death Stranding з'явився після перезапуску Kojima Productions і поставив зв'язок у центр правил. Сем перетинає розколоту Америку, поєднуючи ізольовані спільноти; драбини, мотузки й стежки інших гравців перетворюють особисте зусилля на асинхронну співпрацю. Подорож запитує, що означає стати ближчими, не перебуваючи поруч. Доставка стає жестом довіри до людей, яких ми можемо ніколи не зустріти.",
    ja="再出発したコジマプロダクションから生まれた『DEATH STRANDING』は、つながりをルールの中心に置く。サムは分断されたアメリカを歩き、孤立した共同体を結び直す。他のプレイヤーが残した梯子、ロープ、道は、一人の苦労を非同期の協力へ変える。同じ場所にいなくても近づくとは何か。荷物を届ける行為は、会うことのない誰かへの信頼になる。",
    ko="새로 출발한 코지마 프로덕션에서 나온 데스 스트랜딩은 연결을 규칙의 중심에 둔다. 샘은 분열된 미국을 건너 고립된 공동체를 다시 잇는다. 다른 플레이어가 남긴 사다리와 밧줄, 길은 개인의 수고를 비동기 협력으로 바꾼다. 같은 공간에 있지 않아도 가까워진다는 것은 무엇인가. 화물을 전하는 일은 만나지 못할 사람을 향한 신뢰가 된다.",
    zh="重建后的小岛工作室以《死亡搁浅》把连接放在规则中心。山姆穿越破碎的美国，重新连接孤立社区；其他玩家留下的梯子、绳索与道路，把个人劳作变成异步合作。旅程追问：不在同一空间，人们如何靠近。送达货物也因此成为对那些也许永远不会相见之人的信任。",
    hi="नई शुरुआत के बाद Kojima Productions ने Death Stranding में जुड़ाव को नियमों के केंद्र में रखा। Sam टूटे हुए अमेरिका में अलग समुदायों को जोड़ता है; दूसरे खिलाड़ियों की सीढ़ियाँ, रस्सियाँ और रास्ते अकेली मेहनत को असमकालिक सहयोग बनाते हैं। यात्रा पूछती है कि एक जगह न रहते हुए करीब आने का अर्थ क्या है। सामान पहुँचाना उन लोगों पर भरोसा बनता है जिनसे शायद कभी मुलाकात न हो।")

miyamoto_gameboy = localized(
    it="Il Game Boy nasce dal gruppo di sviluppo guidato da Gunpei Yokoi, non da un'invenzione di Miyamoto. La sua presenza nella storia di Nintendo ricorda però quanto le opere della casa dipendano da competenze che si incrociano: hardware, software, personaggi e abitudini quotidiane. Portare il gioco fuori dal salotto significa progettare per mani, luce, batterie e brevi intervalli. L'autorialità resta parte di una cultura collettiva, capace di continuare oltre un singolo nome.",
    en="The Game Boy came from the development group led by Gunpei Yokoi, not from a Miyamoto invention. Its place in Nintendo's history still shows how the company's work depends on intersecting skills: hardware, software, characters and everyday habits. Taking play beyond the living room means designing for hands, light, batteries and short intervals. Authorship remains part of a collective culture able to continue beyond one name.",
    de="Der Game Boy entstand in der von Gunpei Yokoi geleiteten Entwicklungsgruppe, nicht als Erfindung Miyamotos. Sein Platz in Nintendos Geschichte zeigt dennoch, wie Hardware, Software, Figuren und Alltag zusammenwirken. Spielen außerhalb des Wohnzimmers verlangt Entwürfe für Hände, Licht, Batterien und kurze Pausen. Autorschaft bleibt Teil einer kollektiven Kultur, die über einen Namen hinausgeht.",
    es="Game Boy nació del grupo dirigido por Gunpei Yokoi, no de una invención de Miyamoto. Su lugar en la historia de Nintendo muestra cómo la compañía une hardware, software, personajes y hábitos cotidianos. Llevar el juego fuera del salón exige diseñar para manos, luz, baterías e intervalos breves. La autoría forma parte de una cultura colectiva que continúa más allá de un solo nombre.",
    fr="Le Game Boy est issu du groupe dirigé par Gunpei Yokoi, pas d'une invention de Miyamoto. Sa place dans l'histoire de Nintendo montre pourtant l'entrecroisement du matériel, du logiciel, des personnages et des usages quotidiens. Jouer hors du salon impose de penser aux mains, à la lumière, aux piles et aux moments brefs. L'auteur appartient à une culture collective qui dépasse un seul nom.",
    pt="O Game Boy nasceu do grupo liderado por Gunpei Yokoi, não de uma invenção de Miyamoto. O seu lugar na história da Nintendo mostra como hardware, software, personagens e hábitos cotidianos se cruzam. Levar o jogo para fora da sala exige projetar para mãos, luz, pilhas e pequenos intervalos. A autoria integra uma cultura coletiva que continua além de um único nome.",
    ru="Game Boy создала группа под руководством Гумпэя Ёкои, а не Миямото. Его место в истории Nintendo показывает пересечение аппаратуры, программ, персонажей и повседневных привычек. Игра вне гостиной требует учитывать руки, свет, батареи и короткие промежутки времени. Авторство остаётся частью коллективной культуры, которая живёт дольше одного имени.",
    uk="Game Boy створила група під керівництвом Ґумпея Йокої, а не Міямото. Його місце в історії Nintendo показує перетин апаратури, програм, персонажів і щоденних звичок. Гра поза вітальнею потребує уваги до рук, світла, батарей і коротких проміжків часу. Авторство лишається частиною колективної культури, що живе довше за одне ім'я.",
    ja="ゲームボーイは横井軍平が率いた開発部門から生まれたもので、宮本の発明ではない。しかし任天堂の歴史における存在は、ハード、ソフト、キャラクター、日常の習慣が交差して作品になることを示す。居間の外へ遊びを持ち出すには、手、光、電池、短い時間を考えなければならない。作家性も、一人の名を越えて続く集団文化の一部である。",
    ko="게임보이는 요코이 군페이가 이끈 개발 조직에서 나왔으며 미야모토의 발명이 아니다. 그러나 닌텐도 역사에서 이 기기는 하드웨어와 소프트웨어, 캐릭터와 일상의 습관이 만나 작품이 된다는 사실을 보여 준다. 거실 밖의 놀이는 손과 빛, 배터리와 짧은 시간을 고려해야 한다. 작가성도 한 이름을 넘어 이어지는 집단 문화의 일부다.",
    zh="Game Boy出自横井军平领导的开发团队，并非宫本的发明。但它在任天堂历史中的位置说明，硬件、软件、角色与日常习惯必须彼此交汇。把游戏带出客厅，就要为双手、光线、电池与短暂时间设计。作者性仍属于一种集体文化，并能越过单一姓名继续发展。",
    hi="Game Boy गुनपेई योकोई के नेतृत्व वाले समूह ने बनाया, मियामोटो ने नहीं। फिर भी Nintendo के इतिहास में उसका स्थान बताता है कि हार्डवेयर, सॉफ़्टवेयर, पात्र और रोज़मर्रा की आदतें मिलकर काम बनाती हैं। बैठक से बाहर खेलने के लिए हाथ, रोशनी, बैटरी और छोटे समय को ध्यान में रखना पड़ता है। लेखकता एक सामूहिक संस्कृति का हिस्सा रहती है जो एक नाम से आगे चलती है।")

miyamoto_award = localized(
    it="Nel 2010 BAFTA conferisce a Miyamoto la Fellowship, il suo massimo riconoscimento individuale. Il premio riconosce una carriera che ha reso familiari a milioni di persone azioni come saltare, esplorare e provare ancora. Celebra un percorso creativo senza cancellare le squadre che lo hanno reso possibile. L'autorialità del videogioco si misura anche nelle azioni affidate al pubblico.",
    en="In 2010 BAFTA awarded Miyamoto its Fellowship, its highest individual honour. It recognised a career that made actions such as jumping, exploring and trying again familiar to millions. The award celebrates a creative path without erasing the teams that made it possible. Authorship in games can also be measured through the actions entrusted to the audience.",
    de="2010 erhielt Miyamoto die BAFTA Fellowship, die höchste persönliche Auszeichnung der Akademie. Sie würdigte eine Laufbahn, die Springen, Erkunden und erneutes Versuchen für Millionen vertraut machte. Der Preis feiert einen kreativen Weg, ohne die beteiligten Teams auszublenden. Autorschaft zeigt sich auch in den Handlungen, die sie dem Publikum anvertraut.",
    es="En 2010 BAFTA concedió a Miyamoto la Fellowship, su máximo reconocimiento individual. El premio reconoció una carrera que volvió familiares para millones acciones como saltar, explorar y volver a intentarlo. Celebra una trayectoria sin borrar a los equipos que la hicieron posible. La autoría también se mide por las acciones confiadas al público.",
    fr="En 2010, BAFTA décerne à Miyamoto la Fellowship, sa plus haute distinction individuelle. Elle récompense un parcours qui a rendu familiers à des millions de personnes le saut, l'exploration et le nouvel essai. Le prix célèbre une carrière sans effacer les équipes qui l'ont rendue possible. L'auteur se reconnaît aussi aux actions confiées au public.",
    pt="Em 2010, a BAFTA concedeu a Miyamoto a Fellowship, sua maior distinção individual. O prêmio reconheceu uma carreira que tornou familiares a milhões ações como saltar, explorar e tentar novamente. Celebra uma trajetória sem apagar as equipes que a tornaram possível. A autoria também se mede pelas ações confiadas ao público.",
    ru="В 2010 году BAFTA вручила Миямото Fellowship, свою высшую индивидуальную награду. Она отметила карьеру, сделавшую прыжок, исследование и новую попытку знакомыми миллионам. Награда празднует творческий путь, не стирая заслуг команд. Авторство измеряется и действиями, доверенными публике.",
    uk="2010 року BAFTA вручила Міямото Fellowship, свою найвищу індивідуальну нагороду. Вона відзначила кар'єру, що зробила стрибок, дослідження й нову спробу знайомими мільйонам. Нагорода святкує творчий шлях, не стираючи заслуг команд. Авторство вимірюється й діями, довіреними публіці.",
    ja="2010年、宮本はBAFTAの個人に対する最高栄誉、フェローシップを受けた。跳ぶ、探索する、もう一度試すという行為を何百万人にも親しいものにした歩みへの評価だった。支えたチームを消すことなく、創作の道を讃える。ゲームの作家性は、遊び手に託した行為にも表れる。",
    ko="2010년 BAFTA는 미야모토에게 개인 최고 영예인 펠로십을 수여했다. 뛰고 탐험하고 다시 도전하는 행동을 수백만 명에게 익숙하게 만든 경력을 인정한 것이다. 이를 가능하게 한 팀을 지우지 않으면서 창작의 길을 기린다. 게임의 작가성은 플레이어에게 맡긴 행동으로도 드러난다.",
    zh="2010年，BAFTA向宫本授予其最高个人荣誉Fellowship，表彰他让跳跃、探索与再次尝试成为数百万人熟悉动作的职业生涯。荣誉庆祝创作道路，也不抹去让它成为可能的团队。游戏作者性同样体现在交给玩家的行动中。",
    hi="2010 में BAFTA ने मियामोटो को अपना सर्वोच्च व्यक्तिगत सम्मान Fellowship दिया। इसने उस करियर को पहचाना जिसने कूदना, खोजना और फिर कोशिश करना करोड़ों लोगों के लिए परिचित बनाया। सम्मान रचनात्मक यात्रा और उसे संभव बनाने वाली टीमों, दोनों को मानता है। गेम की लेखकता खिलाड़ी को सौंपी गई क्रियाओं में भी दिखती है।")


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def without_em_dash(value):
    if isinstance(value, dict):
        return {key: without_em_dash(item) for key, item in value.items()}
    if isinstance(value, list):
        return [without_em_dash(item) for item in value]
    if isinstance(value, str):
        return value.replace(" — ", ": ").replace("—", ", ")
    return value


catalog = load(CATALOG)
kojima = next(item for item in catalog["themes"] if item["id"] == "kojima")
old = kojima["chapters"]
youth = next(item for item in old if item.get("image_file") == "kojima-youth.png")
method = next(item for item in old if item.get("title", {}).get("it") == "L’autore entra nell’inquadratura")
method["image_file"] = "kojima-stage.png"
studio = next(item for item in old if item.get("subject") == "Kojima Productions")
death = next(item for item in old if item.get("subject") == "Death Stranding")
death["title"], death["body"] = death_title, death_body
metal_gear_chapters = load(METAL_GEAR_CONTENT)
kojima["chapters"] = [youth, konami, *metal_gear_chapters, method, studio, death]
kojima["sources"] = list(dict.fromkeys((kojima.get("sources") or []) + [
    "https://www.bafta.org/media-centre/press-releases/bafta-annual-games-lecture-2012-hideo-kojima/",
    "https://www.bafta.org/stories/the-fellowship-2020-hideo-kojima/",
    "https://www.konami.com/mg/archive/mgs/about_mgs/index.html",
    "https://www.konami.com/mg/archive/mgs_tlc/",
    "https://www.konami.com/games/eu/en/products/mgsv_tde/",
]))

images = load(IMAGES)
kept = [item for item in images["kojima"] if item["file"] in {
    "kojima-intro.png", "kojima-youth.png", "kojima-cinematic.png",
    "kojima-stage.png", "kojima-productions.png", "death-stranding-game.png",
}]
official_page = "https://www.konami.com/mg/archive/mgs_tlc/"
official_images = [
    ("kojima-metal-gear-msx.jpg", "Metal Gear", official_page),
    ("kojima-mgs-playstation.jpg", "Metal Gear Solid", official_page),
    ("kojima-mgs2-information.jpg", "Metal Gear Solid 2: Sons of Liberty", official_page),
    ("kojima-mgs3-legacy.jpg", "Metal Gear Solid 3: Snake Eater", official_page),
    ("kojima-mgsv-open-world.jpg", "Metal Gear Solid V: The Phantom Pain", "https://store.steampowered.com/app/287700/METAL_GEAR_SOLID_V_THE_PHANTOM_PAIN/"),
]
for filename, subject, page in official_images:
    kept.append({
        "file": filename,
        "url": f"local://{filename}",
        "sourceUrl": page,
        "subject": subject,
        "kind": "screenshot",
        "provider": "Konami",
        "rights": "Game imagery: Konami Digital Entertainment.",
    })
order = ["kojima-intro.png", "kojima-youth.png", "kojima-cinematic.png"] + [x[0] for x in official_images] + [
    "kojima-stage.png", "kojima-productions.png", "death-stranding-game.png"
]
by_file = {item["file"]: item for item in kept}
images["kojima"] = [by_file[name] for name in order]

miyamoto_source = load(MIYAMOTO_SOURCE)
miyamoto_source["chapters"][3]["body"] = miyamoto_gameboy
miyamoto_source["chapters"][4]["body"] = miyamoto_award
miyamoto = next(item for item in catalog["themes"] if item["id"] == "miyamoto")
miyamoto["chapters"] = miyamoto_source["chapters"]

catalog = without_em_dash(catalog)
miyamoto_source = without_em_dash(miyamoto_source)
days = without_em_dash(load(DAYS))
images = without_em_dash(images)

CATALOG.write_text(json.dumps(catalog, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
MIYAMOTO_SOURCE.write_text(json.dumps(miyamoto_source, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
DAYS.write_text(json.dumps(days, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
IMAGES.write_text(json.dumps(images, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print("Expanded Kojima and Metal Gear sequence; removed photograph narration.")

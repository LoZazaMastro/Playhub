import { DFL, SP_REACT as React } from './decky';
import { createOnboardingNativeAdapter } from './onboardingNative';

const copy: Record<string, string[]> = {
  it: ['Premi', 'per approfondire', 'per richiudere', 'Visualizza immagine', 'Chiudi'],
  en: ['Press', 'to explore', 'to collapse', 'View image', 'Close'],
  de: ['Drücke', 'für mehr', 'zum Zuklappen', 'Bild ansehen', 'Schließen'],
  es: ['Pulsa', 'para saber más', 'para contraer', 'Ver imagen', 'Cerrar'],
  fr: ['Appuyez sur', 'pour approfondir', 'pour replier', 'Voir l’image', 'Fermer'],
  pt: ['Pressione', 'para saber mais', 'para recolher', 'Ver imagem', 'Fechar'],
  ru: ['Нажмите', 'чтобы узнать больше', 'чтобы свернуть', 'Открыть изображение', 'Закрыть'],
  uk: ['Натисніть', 'щоб дізнатися більше', 'щоб згорнути', 'Переглянути зображення', 'Закрити'],
  ja: ['', 'で詳しく読む', 'で折りたたむ', '画像を表示', '閉じる'],
  ko: ['', '버튼으로 자세히 보기', '버튼으로 접기', '이미지 보기', '닫기'],
  zh: ['按', '深入閱讀', '收起內容', '查看圖片', '關閉'],
  hi: ['', 'दबाकर विस्तार से पढ़ें', 'दबाकर समेटें', 'चित्र देखें', 'बंद करें'],
};
export const historyActionCopy = (locale: string) => copy[locale.split(/[-_]/)[0]] ?? copy.en;

class GlyphBoundary extends React.Component<React.PropsWithChildren, {failed: boolean}> {
  state = {failed: false};
  static getDerivedStateFromError() { return {failed: true}; }
  render() { return this.state.failed ? <span>A</span> : this.props.children; }
}

export function HistoryConfirmHint({locale, expanded}: {locale: string; expanded: boolean}) {
  const adapter = React.useMemo(() => createOnboardingNativeAdapter(), []);
  const [, refresh] = React.useReducer((value: number) => value + 1, 0);
  React.useEffect(() => adapter.subscribe(refresh), [adapter]);
  const text = historyActionCopy(locale);
  return <span className="ph-history-confirm-hint">{text[0]}{' '}
    <GlyphBoundary>{adapter.renderHint('confirm') ?? <span>A</span>}</GlyphBoundary>{' '}
    {text[expanded ? 2 : 1]}</span>;
}

function FullscreenImage({url, subject, locale, closeModal}: {url: string; subject: string; locale: string; closeModal?: () => void}) {
  const {ModalRoot, Focusable} = DFL as any;
  const close = (event?: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); closeModal?.(); };
  return <ModalRoot closeModal={closeModal} onCancel={close} style={{width:'100vw',maxWidth:'100vw',height:'100vh',padding:0,background:'#000'}}>
    <style>{`
      .DialogContent:has(.ph-history-fullscreen){position:fixed!important;inset:0!important;transform:none!important;margin:0!important;padding:0!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important;background:#000!important;border:0!important;border-radius:0!important;box-sizing:border-box!important;overflow:hidden!important}
      .DialogContent_InnerWidth:has(.ph-history-fullscreen){width:100%!important;max-width:none!important;height:100%!important;margin:0!important;padding:0!important}
      .ph-history-fullscreen{width:100vw!important;height:100vh!important;max-width:100vw!important;max-height:100vh!important;overflow:hidden!important}
      .ph-history-fullscreen img{display:block!important;width:100%!important;height:100%!important;min-width:0!important;min-height:0!important;max-width:100%!important;max-height:100%!important;object-fit:contain!important}
    `}</style>
    <Focusable autoFocus className="ph-history-fullscreen" flow-children="column" onCancel={close} onCancelButton={close}
      style={{width:'100%',height:'100%',display:'flex',alignItems:'center',justifyContent:'center',position:'relative'}}>
      <img src={url} alt={subject} style={{width:'100%',height:'100%',objectFit:'contain'}} />
    </Focusable>
  </ModalRoot>;
}

// Keep image viewing separate from Steam artwork details and sharing actions.
export function openHistoryImage(url: string, subject: string, locale: string, ownerWindow: Window = window) {
  (DFL as any).showModal(<FullscreenImage url={url} subject={subject} locale={locale}/>, ownerWindow, {bNeverPopOut:true});
}

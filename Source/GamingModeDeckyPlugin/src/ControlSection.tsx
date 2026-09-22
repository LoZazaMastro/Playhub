import { SP_REACT as React, DFL } from "./decky";
import { TbChevronDown, TbChevronRight } from "react-icons/tb";
import { controlLocale } from "./controlCenterLocale";
const { DialogButton, Focusable } = DFL as any;
export function ControlSection({ id, title, icon, children, collapsed, onToggle, locale }: {
  id: string; title: string; icon?: React.ReactNode; children: React.ReactNode;
  collapsed: boolean; onToggle: (id: string) => void; locale: string;
}) {
  const copy = controlLocale(locale);
  return <Focusable className="ph-control-section" flow-children="column">
    <DialogButton className="ph-control-section-title" aria-expanded={!collapsed} onClick={() => onToggle(id)} onOKActionDescription={collapsed ? copy.expand : copy.collapse}>
      <span>{icon}<strong>{title}</strong></span>{collapsed ? <TbChevronRight /> : <TbChevronDown />}
    </DialogButton>
    {!collapsed && <Focusable className="ph-control-section-body" flow-children="column">{children}</Focusable>}
  </Focusable>;
}

const manifest = {"name":"Gaming Mode"};
const API_VERSION = 2;
const internalAPIConnection = window.__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit;
if (!internalAPIConnection) {
    throw new Error('[@decky/api]: Failed to connect to the loader as as the loader API was not initialized. This is likely a bug in Decky Loader.');
}
let api;
try {
    api = internalAPIConnection.connect(API_VERSION, manifest.name);
}
catch {
    api = internalAPIConnection.connect(1, manifest.name);
    console.warn(`[@decky/api] Requested API version ${API_VERSION} but the running loader only supports version 1. Some features may not work.`);
}
if (api._version != API_VERSION) {
    console.warn(`[@decky/api] Requested API version ${API_VERSION} but the running loader only supports version ${api._version}. Some features may not work.`);
}
const toaster = api.toaster;
const definePlugin = (fn) => {
    return (...args) => {
        return fn(...args);
    };
};

var DefaultContext = {
  color: undefined,
  size: undefined,
  className: undefined,
  style: undefined,
  attr: undefined
};
var IconContext = SP_REACT.createContext && /*#__PURE__*/SP_REACT.createContext(DefaultContext);

var _excluded = ["attr", "size", "title"];
function _objectWithoutProperties(e, t) { if (null == e) return {}; var o, r, i = _objectWithoutPropertiesLoose(e, t); if (Object.getOwnPropertySymbols) { var n = Object.getOwnPropertySymbols(e); for (r = 0; r < n.length; r++) o = n[r], -1 === t.indexOf(o) && {}.propertyIsEnumerable.call(e, o) && (i[o] = e[o]); } return i; }
function _objectWithoutPropertiesLoose(r, e) { if (null == r) return {}; var t = {}; for (var n in r) if ({}.hasOwnProperty.call(r, n)) { if (-1 !== e.indexOf(n)) continue; t[n] = r[n]; } return t; }
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function ownKeys(e, r) { var t = Object.keys(e); if (Object.getOwnPropertySymbols) { var o = Object.getOwnPropertySymbols(e); r && (o = o.filter(function (r) { return Object.getOwnPropertyDescriptor(e, r).enumerable; })), t.push.apply(t, o); } return t; }
function _objectSpread(e) { for (var r = 1; r < arguments.length; r++) { var t = null != arguments[r] ? arguments[r] : {}; r % 2 ? ownKeys(Object(t), true).forEach(function (r) { _defineProperty(e, r, t[r]); }) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(t)) : ownKeys(Object(t)).forEach(function (r) { Object.defineProperty(e, r, Object.getOwnPropertyDescriptor(t, r)); }); } return e; }
function _defineProperty(e, r, t) { return (r = _toPropertyKey(r)) in e ? Object.defineProperty(e, r, { value: t, enumerable: true, configurable: true, writable: true }) : e[r] = t, e; }
function _toPropertyKey(t) { var i = _toPrimitive(t, "string"); return "symbol" == typeof i ? i : i + ""; }
function _toPrimitive(t, r) { if ("object" != typeof t || !t) return t; var e = t[Symbol.toPrimitive]; if (void 0 !== e) { var i = e.call(t, r); if ("object" != typeof i) return i; throw new TypeError("@@toPrimitive must return a primitive value."); } return ("string" === r ? String : Number)(t); }
function Tree2Element(tree) {
  return tree && tree.map((node, i) => /*#__PURE__*/SP_REACT.createElement(node.tag, _objectSpread({
    key: i
  }, node.attr), Tree2Element(node.child)));
}
function GenIcon(data) {
  return props => /*#__PURE__*/SP_REACT.createElement(IconBase, _extends({
    attr: _objectSpread({}, data.attr)
  }, props), Tree2Element(data.child));
}
function IconBase(props) {
  var elem = conf => {
    var {
        attr,
        size,
        title
      } = props,
      svgProps = _objectWithoutProperties(props, _excluded);
    var computedSize = size || conf.size || "1em";
    var className;
    if (conf.className) className = conf.className;
    if (props.className) className = (className ? className + " " : "") + props.className;
    return /*#__PURE__*/SP_REACT.createElement("svg", _extends({
      stroke: "currentColor",
      fill: "currentColor",
      strokeWidth: "0"
    }, conf.attr, attr, svgProps, {
      className: className,
      style: _objectSpread(_objectSpread({
        color: props.color || conf.color
      }, conf.style), props.style),
      height: computedSize,
      width: computedSize,
      xmlns: "http://www.w3.org/2000/svg"
    }), title && /*#__PURE__*/SP_REACT.createElement("title", null, title), props.children);
  };
  return IconContext !== undefined ? /*#__PURE__*/SP_REACT.createElement(IconContext.Consumer, null, conf => elem(conf)) : elem(DefaultContext);
}

// THIS FILE IS AUTO GENERATED
function FaGamepad (props) {
  return GenIcon({"attr":{"viewBox":"0 0 640 512"},"child":[{"tag":"path","attr":{"d":"M480.07 96H160a160 160 0 1 0 114.24 272h91.52A160 160 0 1 0 480.07 96zM248 268a12 12 0 0 1-12 12h-52v52a12 12 0 0 1-12 12h-24a12 12 0 0 1-12-12v-52H84a12 12 0 0 1-12-12v-24a12 12 0 0 1 12-12h52v-52a12 12 0 0 1 12-12h24a12 12 0 0 1 12 12v52h52a12 12 0 0 1 12 12zm216 76a40 40 0 1 1 40-40 40 40 0 0 1-40 40zm64-96a40 40 0 1 1 40-40 40 40 0 0 1-40 40z"},"child":[]}]})(props);
}

const API_BASE = "http://127.0.0.1:47991";
const strings = {
    en: {
        mode: "Mode",
        switchGaming: "Switch to Gaming Mode",
        switchDesktop: "Switch to Desktop Mode",
        defaultStartup: "Default startup",
        desktopMode: "Desktop Mode",
        gamingMode: "Gaming Mode",
        notConnected: "Agent not connected",
        agentReturned: "Agent returned",
    },
    it: {
        mode: "Modalità",
        switchGaming: "Passa alla modalità Gaming",
        switchDesktop: "Passa alla modalità Desktop",
        defaultStartup: "Avvio predefinito",
        desktopMode: "Modalità Desktop",
        gamingMode: "Modalità Gaming",
        notConnected: "Agent non collegato",
        agentReturned: "Agent ha risposto",
    },
    es: {
        mode: "Modo",
        switchGaming: "Cambiar al modo Gaming",
        switchDesktop: "Cambiar al modo Escritorio",
        defaultStartup: "Inicio predeterminado",
        desktopMode: "Modo Escritorio",
        gamingMode: "Modo Gaming",
        notConnected: "Agente no conectado",
        agentReturned: "El agente devolvió",
    },
    fr: {
        mode: "Mode",
        switchGaming: "Passer en mode Gaming",
        switchDesktop: "Passer en mode Bureau",
        defaultStartup: "Démarrage par défaut",
        desktopMode: "Mode Bureau",
        gamingMode: "Mode Gaming",
        notConnected: "Agent non connecté",
        agentReturned: "Agent a renvoyé",
    },
    de: {
        mode: "Modus",
        switchGaming: "In den Gaming-Modus wechseln",
        switchDesktop: "In den Desktop-Modus wechseln",
        defaultStartup: "Standardstart",
        desktopMode: "Desktop-Modus",
        gamingMode: "Gaming-Modus",
        notConnected: "Agent nicht verbunden",
        agentReturned: "Agent meldete",
    },
    pt: {
        mode: "Modo",
        switchGaming: "Mudar para modo Gaming",
        switchDesktop: "Mudar para modo Desktop",
        defaultStartup: "Arranque predefinido",
        desktopMode: "Modo Desktop",
        gamingMode: "Modo Gaming",
        notConnected: "Agente não ligado",
        agentReturned: "Agente devolveu",
    },
};
function t() {
    const language = navigator.language.split("-")[0];
    return strings[language] ?? strings.en;
}
async function getStatus() {
    const response = await fetch(`${API_BASE}/status`);
    if (!response.ok) {
        throw new Error(`${t().agentReturned} ${response.status}`);
    }
    return await response.json();
}
async function post(path) {
    const response = await fetch(`${API_BASE}${path}`, {
        method: "POST",
    });
    if (!response.ok) {
        throw new Error(`${t().agentReturned} ${response.status}`);
    }
    return await response.json();
}
function Content() {
    const local = t();
    const [status, setStatus] = SP_REACT.useState();
    const [busy, setBusy] = SP_REACT.useState(false);
    const defaultOptions = SP_REACT.useMemo(() => [
        { data: "Desktop", label: local.desktopMode },
        { data: "Gaming", label: local.gamingMode },
    ], [local.desktopMode, local.gamingMode]);
    const refresh = async () => {
        try {
            setStatus(await getStatus());
        }
        catch (error) {
            setStatus(undefined);
            toaster.toast({
                title: "Gaming Mode",
                body: error instanceof Error ? error.message : local.notConnected,
            });
        }
    };
    const run = async (path, title) => {
        setBusy(true);
        try {
            const result = await post(path);
            toaster.toast({
                title,
                body: result.message,
            });
            if (result.status) {
                setStatus(result.status);
            }
            else {
                await refresh();
            }
        }
        catch (error) {
            toaster.toast({
                title,
                body: error instanceof Error ? error.message : local.notConnected,
            });
        }
        finally {
            setBusy(false);
        }
    };
    const setDefault = async (option) => {
        const mode = option.data;
        await run(mode === "Gaming" ? "/default/gaming" : "/default/desktop", local.defaultStartup);
    };
    SP_REACT.useEffect(() => {
        refresh();
        const timer = window.setInterval(refresh, 5000);
        return () => window.clearInterval(timer);
    }, []);
    return (SP_JSX.jsxs(DFL.PanelSection, { title: local.mode, children: [SP_JSX.jsx(DFL.PanelSectionRow, { children: SP_JSX.jsx(DFL.ButtonItem, { disabled: busy, layout: "below", onClick: () => run("/mode/gaming/switch", local.gamingMode), children: local.switchGaming }) }), SP_JSX.jsx(DFL.PanelSectionRow, { children: SP_JSX.jsx(DFL.ButtonItem, { disabled: busy, layout: "below", onClick: () => run("/mode/desktop/switch", local.desktopMode), children: local.switchDesktop }) }), SP_JSX.jsx(DFL.PanelSectionRow, { children: SP_JSX.jsx(DFL.DropdownItem, { label: local.defaultStartup, disabled: busy, rgOptions: defaultOptions, selectedOption: status?.defaultMode ?? "Desktop", onChange: setDefault }) })] }));
}
var index = definePlugin(() => {
    return {
        name: "Gaming Mode",
        titleView: SP_JSX.jsx("div", { className: DFL.staticClasses.Title, children: "Gaming Mode" }),
        content: SP_JSX.jsx(Content, {}),
        icon: SP_JSX.jsx(FaGamepad, {}),
    };
});

export { index as default };
//# sourceMappingURL=index.js.map

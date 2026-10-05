(async function(){
// La scoperta di Steam deriva da ShelvesHub d237922 (MIT, vendor/ShelvesHub-LICENSE).
// @decky/ui continua a risolvere i controlli nativi: nessun componente sostitutivo.
async function bootstrapPlayhubStandalone() {
  const config = window.__PLAYHUB_HOST_CONFIG__;
  if (!config?.baseUrl || !config?.token) throw new Error('Missing Playhub host configuration.');
  if (window.__PLAYHUB_STANDALONE__ && window.__PLAYHUB_STANDALONE__.state !== 'disposed')
    throw new Error('Playhub standalone is already mounted.');
  const endpoint = new URL(config.baseUrl);
  if (endpoint.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(endpoint.hostname))
    throw new Error('The Playhub host must be loopback.');
  const owned = new Map();
  const names = ['SP_REACT', 'SP_REACTDOM', 'SP_JSX'];
  const deckyApi = '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
  let cancelled = false;
  const boot = { state: 'waiting', instanceId: config.instanceId, injectionId: config.injectionId,
    cancel() { cancelled = true; boot.state = 'disposed'; } };
  window.__PLAYHUB_BOOTSTRAP__ = boot;
  const send = async (path, body) => {
    const response = await fetch(endpoint.origin + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + config.token },
      body: JSON.stringify(body),
    });
    if (!response.ok) throw new Error('Playhub host HTTP ' + response.status);
    return response.json();
  };
  const host = {
    call: async (method, ...args) => {
      const result = await send('/rpc', { method, args });
      if (!result.ok) throw new Error(result.error || 'Playhub RPC failed.');
      return result.result;
    },
    event: data => send('/runtime-event', { ...data, instanceId: config.instanceId, injectionId: config.injectionId }).catch(() => {}),
    hasForeignRenderer: () => !!window[deckyApi] || !!window.deckyLoader || names.some(name =>
      window[name] !== undefined && (!owned.has(name) || window[name] !== owned.get(name))),
    releaseOwnedGlobals() {
      // Decky puo' avere sostituito un valore dopo il bootstrap. In quel caso
      // non cancelliamo il suo global solo perche' prima quel nome era nostro.
      // Se Decky subentra puo' riusare gli stessi oggetti React: in quel caso
      // neppure l'identita' prova che il valore sia ancora esclusivamente nostro.
      const deckyTookOver = !!window[deckyApi] || !!window.deckyLoader;
      for (const [name, value] of owned) {
        try { if (!deckyTookOver && window[name] === value) delete window[name]; } catch {}
      }
      owned.clear();
      host.ownedGlobals = [];
    },
    ownedGlobals: [],
  };
  window.__PLAYHUB_HOST__ = host;
  let lastReason = '';
  const waiting = async reason => {
    if (reason !== lastReason) { lastReason = reason; await host.event({ state: 'waiting', reason }); }
    await new Promise(resolve => setTimeout(resolve, 500));
  };
  let req;
  const modules = new Map();
  let variants = [];
  let scanCount = -1;
  let runtime;
  while (true) {
    if (cancelled || window.__PLAYHUB_HOST_CONFIG__ !== config) throw new Error('Playhub bootstrap cancelled.');
    if (host.hasForeignRenderer()) {
      boot.state = 'blocked';
      await host.event({ state: 'blocked', reason: 'Another renderer owns the Steam globals.' });
      throw new Error('Another renderer owns the Steam globals.');
    }
    if (!req) {
      const chunkKey = Object.keys(window).find(key => key.startsWith('webpackChunk') && Array.isArray(window[key]));
      if (chunkKey) window[chunkKey].push([[Symbol('playhub.standalone.discovery')], {}, value => { req = value; }]);
    }
    if (!req?.m) { await waiting('Steam webpack is not available yet.'); continue; }
    const ids = Object.keys(req.m);
    if (!runtime || scanCount !== ids.length) {
      const remember = (id, value) => { if (value) modules.set(id, value); };
      for (const [id, mod] of Object.entries(req.c || {})) remember(id, mod?.exports);
      // La build verificata non espone req.c: si conservano i risultati di
      // req(id). La mancanza di una finestra non fa ripetere questa scansione.
      for (const id of ids) {
        if (modules.has(id)) continue;
        try { remember(id, req(id)); } catch {}
      }
      variants = [];
      const unique = new Set();
      const add = value => { if (value && !unique.has(value)) { unique.add(value); variants.push(value); } };
      for (const value of modules.values()) {
        for (const candidate of [value, value?.default]) {
          add(candidate);
          if (candidate && (typeof candidate === 'object' || typeof candidate === 'function'))
            for (const key of Object.keys(candidate)) try { add(candidate[key]); } catch {}
        }
      }
      const find = predicate => variants.find(value => { try { return predicate(value); } catch { return false; } });
      const React = find(value => typeof value?.createElement === 'function' && typeof value?.useState === 'function');
      const dom = find(value => typeof value?.createPortal === 'function');
      const client = find(value => typeof value?.createRoot === 'function');
      if (React && (client?.createRoot || dom?.render)) {
        const jsx = find(value => typeof value?.jsx === 'function' && typeof value?.jsxs === 'function') || {
          Fragment: React.Fragment,
          jsx: (type, props, key) => React.createElement(type, key === undefined ? props : { ...props, key }),
          jsxs: (type, props, key) => React.createElement(type, key === undefined ? props : { ...props, key }),
        };
        runtime = { React, ReactDOM: { ...dom, ...client }, jsx,
          router: find(value => value?.Navigate && value?.NavigationManager) || window.SteamUIStore };
      }
      scanCount = ids.length;
    }
    if (!runtime) { await waiting('Steam React renderer is not available yet.'); continue; }
    const candidates = [];
    const add = instance => {
      for (const value of [instance, instance?.window, instance?.m_Window, instance?.m_popup,
        instance?.m_BrowserWindow, instance?.BrowserWindow])
        try { if (value?.document?.body && !candidates.includes(value.document)) candidates.push(value.document); } catch {}
    };
    const store = runtime.router?.WindowStore || window.SteamUIStore?.WindowStore;
    add(store?.GamepadUIMainWindowInstance);
    for (const value of store?.SteamUIWindows || []) add(value);
    try { for (const value of window.g_PopupManager?.GetPopups?.() || []) add(value); } catch {}
    if (document.body && !/SharedJSContext/i.test(document.title)) candidates.push(document);
    const targetDocument = candidates.find(doc => !/SharedJSContext/i.test(doc.title));
    if (!targetDocument) { await waiting('The visible Steam window is not available yet.'); continue; }
    if (host.hasForeignRenderer()) continue;
    const provide = (name, value) => {
      if (value && window[name] === undefined) { window[name] = value; owned.set(name, value); }
    };
    provide('SP_REACT', runtime.React);
    provide('SP_REACTDOM', runtime.ReactDOM);
    provide('SP_JSX', runtime.jsx);
    Object.assign(host, runtime, {
      targetDocument, targetDocuments: candidates, moduleExports: variants, ownedGlobals: [...owned.keys()],
      diagnostics: { moduleCount: modules.size, documentTitle: targetDocument.title },
    });
    boot.state = 'bootstrapped';
    return;
  }
}

await bootstrapPlayhubStandalone();
(function () {
    'use strict';

    const bgStyle1 = 'background: #16a085; color: black;';
    const log = (name, ...args) => {
        console.log(`%c @decky/ui %c ${name} %c`, bgStyle1, 'background: #1abc9c; color: black;', 'background: transparent;', ...args);
    };
    const group = (name, ...args) => {
        console.group(`%c @decky/ui %c ${name} %c`, bgStyle1, 'background: #1abc9c; color: black;', 'background: transparent;', ...args);
    };
    const groupEnd = (name, ...args) => {
        console.groupEnd();
        if (args?.length > 0)
            console.log(`^ %c @decky/ui %c ${name} %c`, bgStyle1, 'background: #1abc9c; color: black;', 'background: transparent;', ...args);
    };
    const debug = (name, ...args) => {
        console.debug(`%c @decky/ui %c ${name} %c`, bgStyle1, 'background: #1abc9c; color: black;', 'color: blue;', ...args);
    };
    const warn$1 = (name, ...args) => {
        console.warn(`%c @decky/ui %c ${name} %c`, bgStyle1, 'background: #ffbb00; color: black;', 'color: blue;', ...args);
    };
    const error = (name, ...args) => {
        console.error(`%c @decky/ui %c ${name} %c`, bgStyle1, 'background: #FF0000;', 'background: transparent;', ...args);
    };
    class Logger {
        constructor(name) {
            this.name = name;
            this.name = name;
        }
        log(...args) {
            log(this.name, ...args);
        }
        debug(...args) {
            debug(this.name, ...args);
        }
        warn(...args) {
            warn$1(this.name, ...args);
        }
        error(...args) {
            error(this.name, ...args);
        }
        group(...args) {
            group(this.name, ...args);
        }
        groupEnd(...args) {
            groupEnd(this.name, ...args);
        }
    }

    const logger$2 = new Logger('Webpack');
    let modules = new Map();
    function initModuleCache() {
        const startTime = performance.now();
        logger$2.group('Webpack Module Init');
        const id = Symbol("@decky/ui");
        let webpackRequire;
        window.webpackChunksteamui.push([
            [id],
            {},
            (r) => {
                webpackRequire = r;
            },
        ]);
        logger$2.log('Initializing all modules. Errors here likely do not matter, as they are usually just failing module side effects.');
        for (let id of Object.keys(webpackRequire.m)) {
            try {
                const module = webpackRequire(id);
                if (module) {
                    modules.set(id, module);
                }
            }
            catch (e) {
                logger$2.debug('Ignoring require error for module', id, e);
            }
        }
        logger$2.groupEnd(`Modules initialized in ${performance.now() - startTime}ms...`);
    }
    initModuleCache();
    const findModule = (filter) => {
        for (const m of modules.values()) {
            if (m.default && filter(m.default))
                return m.default;
            if (filter(m))
                return m;
        }
    };
    const findModuleDetailsByExport = (filter, minExports) => {
        for (const [id, m] of modules) {
            if (!m)
                continue;
            for (const mod of [m.default, m]) {
                if (typeof mod !== 'object')
                    continue;
                if (mod == window)
                    continue;
                if (minExports && Object.keys(mod).length < minExports)
                    continue;
                for (let exportName in mod) {
                    if (mod?.[exportName]) {
                        try {
                            const filterRes = filter(mod[exportName], exportName);
                            if (filterRes) {
                                return [mod, mod[exportName], exportName, id];
                            }
                            else {
                                continue;
                            }
                        }
                        catch (e) {
                            logger$2.warn("Webpack filter threw exception: ", e);
                        }
                    }
                }
            }
        }
        return [undefined, undefined, undefined, undefined];
    };
    const findModuleByExport = (filter, minExports) => {
        return findModuleDetailsByExport(filter, minExports)?.[0];
    };
    const findModuleExport = (filter, minExports) => {
        return findModuleDetailsByExport(filter, minExports)?.[1];
    };
    const findModuleChild = (filter) => {
        logger$2.warn("findModuleChild is deprecated and will be removed soon. Use findModuleExport instead. Used in:", new Error().stack?.substring(5));
        for (const m of modules.values()) {
            for (const mod of [m.default, m]) {
                const filterRes = filter(mod);
                if (filterRes) {
                    return filterRes;
                }
                else {
                    continue;
                }
            }
        }
    };
    const findAllModules = (filter) => {
        logger$2.warn("findAllModules is deprecated and will be removed soon. Use createModuleMapping instead. Used in:", new Error().stack?.substring(5));
        const out = [];
        for (const m of modules.values()) {
            if (m.default && filter(m.default))
                out.push(m.default);
            if (filter(m))
                out.push(m);
        }
        return out;
    };
    const createModuleMapping = (filter) => {
        const mapping = new Map();
        for (const [id, m] of modules) {
            if (m.default && filter(m.default))
                mapping.set(id, m.default);
            if (filter(m))
                mapping.set(id, m);
        }
        return mapping;
    };
    const CommonUIModule = findModule((m) => {
        if (typeof m !== 'object')
            return false;
        for (let prop in m) {
            if (m[prop]?.contextType?._currentValue && Object.keys(m).length > 60)
                return true;
        }
        return false;
    });
    const IconsModule = findModuleByExport((e) => e?.toString && /Spinner\),children:\[\(0,\w+\.jsx\)\("path",\{d:"M18 /.test(e.toString()) || /Spinner\)}\)?,.\.createElement\(\"path\",{d:\"M18 /.test(e.toString()));
    const ReactRouter = findModuleByExport((e) => e.computeRootMatch);

    const CommonDialogDivs = Object.values(CommonUIModule).filter((m) => typeof m === 'object' &&
        (m?.render?.toString().includes('jsx)("div",{...') ||
            m?.render?.toString().includes('jsx)("div",Object.assign({},')) ||
        (m?.render?.toString().includes('createElement("div",{...') ||
            m?.render?.toString().includes('createElement("div",Object.assign({},')));
    const MappedDialogDivs = new Map(Object.values(CommonDialogDivs).map((m) => {
        try {
            const renderedDiv = m.render({});
            return [renderedDiv.props.className.split(' ')[0], m];
        }
        catch (e) {
            console.error("[DFL:Dialog]: failed to render common dialog component", e);
            return [null, null];
        }
    }));
    const DialogHeader = (MappedDialogDivs.get('DialogHeader') || Object.values(CommonUIModule).find((component) => {
        const str = component?.render?.toString?.();
        return str?.includes("role:\"heading\"") && str.includes(")(\"DialogHeader\",");
    }));
    const DialogSubHeader = MappedDialogDivs.get('DialogSubHeader');
    const DialogFooter = MappedDialogDivs.get('DialogFooter');
    const DialogLabel = MappedDialogDivs.get('DialogLabel');
    const DialogBodyText = MappedDialogDivs.get('DialogBodyText');
    const DialogBody = MappedDialogDivs.get('DialogBody');
    const DialogControlsSection = MappedDialogDivs.get('DialogControlsSection');
    const DialogControlsSectionHeader = MappedDialogDivs.get('DialogControlsSectionHeader');
    const DialogButtonPrimary = Object.values(CommonUIModule).find((mod) => mod?.render?.toString?.()?.includes('"DialogButton","_DialogLayout","Primary"'));
    const DialogButtonSecondary = Object.values(CommonUIModule).find((mod) => mod?.render?.toString?.()?.includes('"DialogButton","_DialogLayout","Secondary"'));
    const DialogButton = DialogButtonSecondary;

    const Button = DialogButton?.render({}).type;

    let callOriginal = Symbol('DECKY_CALL_ORIGINAL');
    function beforePatch(object, property, handler, options = {}) {
        const orig = object[property];
        object[property] = function (...args) {
            handler.call(this, args);
            const ret = patch.original.call(this, ...args);
            if (options.singleShot) {
                patch.unpatch();
            }
            return ret;
        };
        const patch = processPatch(object, property, handler, object[property], orig);
        return patch;
    }
    function afterPatch(object, property, handler, options = {}) {
        const orig = object[property];
        object[property] = function (...args) {
            let ret = patch.original.call(this, ...args);
            ret = handler.call(this, args, ret);
            if (options.singleShot) {
                patch.unpatch();
            }
            return ret;
        };
        const patch = processPatch(object, property, handler, object[property], orig);
        return patch;
    }
    function replacePatch(object, property, handler, options = {}) {
        const orig = object[property];
        object[property] = function (...args) {
            const ret = handler.call(this, args);
            if (ret == callOriginal)
                return patch.original.call(this, ...args);
            if (options.singleShot) {
                patch.unpatch();
            }
            return ret;
        };
        const patch = processPatch(object, property, handler, object[property], orig);
        return patch;
    }
    function processPatch(object, property, handler, patchedFunction, original) {
        Object.assign(object[property], original);
        object[property].toString = () => original.toString();
        Object.defineProperty(object[property], '__deckyOrig', {
            get: () => patch.original,
            set: (val) => (patch.original = val),
        });
        const patch = {
            object,
            property,
            handler,
            patchedFunction,
            original,
            hasUnpatched: false,
            unpatch: () => unpatch(patch),
        };
        object[property].__deckyPatch = patch;
        return patch;
    }
    function unpatch(patch) {
        const { object, property, handler, patchedFunction, original } = patch;
        if (patch.hasUnpatched)
            throw new Error('Function is already unpatched.');
        let realProp = property;
        let realObject = object;
        console.debug('[Patcher] unpatching', {
            realObject,
            realProp,
            object,
            property,
            handler,
            patchedFunction,
            original,
            isEqual: realObject[realProp] === patchedFunction,
        });
        while (realObject[realProp] && realObject[realProp] !== patchedFunction) {
            realObject = realObject[realProp].__deckyPatch;
            realProp = 'original';
            console.debug('[Patcher] moved to next', {
                realObject,
                realProp,
                object,
                property,
                handler,
                patchedFunction,
                original,
                isEqual: realObject[realProp] === patchedFunction,
            });
        }
        realObject[realProp] = realObject[realProp].__deckyPatch.original;
        patch.hasUnpatched = true;
        console.debug('[Patcher] unpatched', {
            realObject,
            realProp,
            object,
            property,
            handler,
            patchedFunction,
            original,
            isEqual: realObject[realProp] === patchedFunction,
        });
    }

    const classModuleMap = createModuleMapping((m) => {
        if (typeof m == 'object' && !m.__esModule) {
            const keys = Object.keys(m);
            if (keys.length == 1 && m.version)
                return false;
            if (keys.length > 1000 && m.AboutSettings)
                return false;
            return keys.length > 0 && keys.every((k) => !Object.getOwnPropertyDescriptor(m, k)?.get && typeof m[k] == 'string');
        }
        return false;
    });
    const classMap = [...classModuleMap.values()];
    function findClass(id, name) {
        return classModuleMap.get(id)?.[name];
    }
    function findClassByName(name) {
        return classMap.find((m) => m[name])?.[name];
    }
    function findClassModule(filter) {
        return classMap.find((m) => filter(m));
    }
    function unminifyClass(minifiedClass) {
        for (let m of classModuleMap.values()) {
            for (let className of Object.keys(m)) {
                if (m[className] == minifiedClass)
                    return className;
            }
        }
    }

    const quickAccessMenuClasses = findClassModule((m) => m.Title && m.QuickAccessMenu && m.BatteryDetailsLabels);
    const scrollPanelClasses = findClassModule((m) => m.ScrollPanel);
    const gamepadDialogClasses = findClassModule((m) => m.GamepadDialogContent && !m.BindingButtons);
    const quickAccessControlsClasses = findClassModule((m) => m.BatteryPercentageLabel && m.PanelSection && !m['vr-dashboard-bar-height'] && !m.QuickAccessMenu && !m.QuickAccess && !m.PerfProfileInfo);
    const updaterFieldClasses = findClassModule((m) => m.OOBEUpdateStatusContainer);
    const playSectionClasses = findClassModule((m) => m.PlayBarDetailLabel);
    const gamepadSliderClasses = findClassModule((m) => m.SliderControlPanelGroup);
    const appDetailsHeaderClasses = findClassModule((m) => m.TopCapsule);
    const appDetailsClasses = findClassModule((m) => m.HeaderLoaded);
    const gamepadUIClasses = findClassModule((m) => m.BasicUiRoot);
    const gamepadTabbedPageClasses = findClassModule((m) => m.GamepadTabbedPage);
    const gamepadContextMenuClasses = findClassModule((m) => m.BasicContextMenuModal);
    const achievementListClasses = findClassModule((m) => m.AchievementListItemBase && !m.Page);
    const achievementPageClasses = findClassModule((m) => m.AchievementListItemBase && m.Page);
    const mainMenuAppRunningClasses = findClassModule((m) => m.AppRunningControls && m.OverlayAchievements);
    const basicAppDetailsSectionStylerClasses = findClassModule((m) => m.AppDetailsRoot);
    const steamSpinnerClasses = findClassModule(m => m.SpinnerLoaderContainer);
    const footerClasses = findClassModule(m => m.QuickAccessFooter);
    const appActionButtonClasses = findClassModule(m => m.PlayButtonContainer);
    const libraryAssetImageClasses = findClassModule(m => m.LongTitles && m.GreyBackground);
    const gamepadLibraryClasses = findClassModule(m => m.GamepadLibrary);
    const focusRingClasses = findClassModule(m => m.FocusRingRoot);
    const searchBarClasses = findClassModule(m => m.SearchAndTitleContainer);
    const mainBrowserClasses = findClassModule(m => m.MainBrowserContainer);
    const staticClasses = quickAccessMenuClasses;
    const scrollClasses = scrollPanelClasses;
    const achievementClasses = achievementListClasses;

    var __setFunctionName = (undefined && undefined.__setFunctionName) || function (f, name, prefix) {
        if (typeof name === "symbol") name = name.description ? "[".concat(name.description, "]") : "";
        return Object.defineProperty(f, "name", { configurable: true, value: prefix ? "".concat(prefix, " ", name) : name });
    };

    function createPropListRegex(propList, fromStart = true) {
        let regexString = fromStart ? "const\{" : "";
        propList.forEach((prop, propIdx) => {
            regexString += `"?${prop}"?:[a-zA-Z_$]{1,2}`;
            if (propIdx < propList.length - 1) {
                regexString += ",";
            }
        });
        return new RegExp(regexString);
    }
    let oldHooks = {};
    let INTERNAL_HOOKS = window.SP_REACT?.__SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED?.ReactCurrentDispatcher
        .current || Object.values(window.SP_REACT?.__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE).find((p) => p?.useEffect);
    function applyHookStubs(customHooks = {}) {
        const hooks = INTERNAL_HOOKS;
        oldHooks = {
            useContext: hooks.useContext,
            useCallback: hooks.useCallback,
            useLayoutEffect: hooks.useLayoutEffect,
            useEffect: hooks.useEffect,
            useMemo: hooks.useMemo,
            useRef: hooks.useRef,
            useState: hooks.useState,
        };
        hooks.useCallback = (cb) => cb;
        hooks.useContext = (cb) => cb?._currentValue;
        hooks.useLayoutEffect = (_) => { };
        hooks.useMemo = (cb, _) => cb;
        hooks.useEffect = (_) => { };
        hooks.useRef = (val) => ({ current: val || {} });
        hooks.useState = (v) => {
            let val = v;
            return [val, (n) => (val = n)];
        };
        Object.assign(hooks, customHooks);
        return hooks;
    }
    function removeHookStubs() {
        const hooks = INTERNAL_HOOKS;
        Object.assign(hooks, oldHooks);
        oldHooks = {};
    }
    function fakeRenderComponent(fun, customHooks) {
        const hooks = applyHookStubs(customHooks);
        const res = fun(hooks);
        removeHookStubs();
        return res;
    }
    function wrapReactType(node, prop = 'type') {
        if (node[prop]?.__DECKY_WRAPPED) {
            return node[prop];
        }
        else {
            return (node[prop] = { ...node[prop], __DECKY_WRAPPED: true });
        }
    }
    function wrapReactClass(node, prop = 'type') {
        var _a;
        if (node[prop]?.__DECKY_WRAPPED) {
            return node[prop];
        }
        else {
            const cls = node[prop];
            const wrappedCls = (_a = class extends cls {
                },
                __setFunctionName(_a, "wrappedCls"),
                _a.__DECKY_WRAPPED = true,
                _a);
            return (node[prop] = wrappedCls);
        }
    }
    function getReactRoot(o) {
        return (o[Object.keys(o).find((k) => k.startsWith('__reactContainer$'))] ||
            o['_reactRootContainer']?._internalRoot?.current);
    }
    function getReactInstance(o) {
        return (o[Object.keys(o).find((k) => k.startsWith('__reactFiber'))] ||
            o[Object.keys(o).find((k) => k.startsWith('__reactInternalInstance'))]);
    }
    const findInTree = (parent, filter, opts) => {
        const { walkable = null, ignore = [] } = opts ?? {};
        if (!parent || typeof parent !== 'object') {
            return null;
        }
        if (filter(parent))
            return parent;
        if (Array.isArray(parent)) {
            return parent.map((x) => findInTree(x, filter, opts)).find((x) => x);
        }
        return (walkable || Object.keys(parent))
            .map((x) => !ignore.includes(x) && findInTree(parent[x], filter, opts))
            .find((x) => x);
    };
    const findInReactTree = (node, filter) => findInTree(node, filter, {
        walkable: ['props', 'children', 'child', 'sibling'],
    });
    function getParentWindow(elem) {
        return elem?.ownerDocument?.defaultView;
    }
    function useWindowRef() {
        const [win, setWin] = window.__PLAYHUB_HOST__.React.useState(null);
        return [(elem) => setWin(getParentWindow(elem)), win];
    }

    let loggingEnabled$1 = false;
    function setFCTrampolineLoggingEnabled(value = true) { loggingEnabled$1 = value; }
    let logger$1 = new Logger('FCTrampoline');
    function injectFCTrampoline(component, customHooks) {
        const newComponent = function (...args) {
            loggingEnabled$1 && logger$1.debug("new component rendering with props", args);
            return component.apply(this, args);
        };
        const userComponent = { component: newComponent };
        component.prototype.render = function (...args) {
            loggingEnabled$1 && logger$1.debug("rendering trampoline", args, this);
            return window.__PLAYHUB_HOST__.React.createElement(userComponent.component, this.props, this.props.children);
        };
        component.prototype.isReactComponent = true;
        let stubsApplied = false;
        const patchJsx = window.SP_REACTDOM.version.startsWith("19.");
        let oldJsx = window.SP_JSX?.jsx;
        let oldJsxs = window.SP_JSX?.jsxs;
        let oldCreateElement = window.SP_REACT.createElement;
        const applyStubsIfNeeded = () => {
            if (!stubsApplied) {
                loggingEnabled$1 && logger$1.debug("applied stubs");
                stubsApplied = true;
                applyHookStubs(customHooks);
                window.SP_REACT.createElement = () => {
                    loggingEnabled$1 && logger$1.debug("createElement hook called");
                    loggingEnabled$1 && console.trace("createElement trace");
                    return Object.create(component.prototype);
                };
                if (patchJsx) {
                    window.SP_JSX.jsx = () => {
                        loggingEnabled$1 && logger$1.debug("jsx hook called");
                        loggingEnabled$1 && console.trace("jsx trace");
                        return Object.create(component.prototype);
                    };
                    window.SP_JSX.jsxs = () => {
                        loggingEnabled$1 && logger$1.debug("jsxs hook called");
                        loggingEnabled$1 && console.trace("jsxs trace");
                        return Object.create(component.prototype);
                    };
                }
            }
        };
        const removeStubsIfNeeded = () => {
            if (stubsApplied) {
                loggingEnabled$1 && logger$1.debug("removed stubs");
                stubsApplied = false;
                removeHookStubs();
                window.SP_REACT.createElement = oldCreateElement;
                if (patchJsx) {
                    window.SP_JSX.jsx = oldJsx;
                    window.SP_JSX.jsxs = oldJsxs;
                }
            }
        };
        let renderHookStep = 0;
        if (window.SP_REACTDOM.version.startsWith("19.")) {
            Object.defineProperty(component, "contextType", {
                configurable: true,
                get: function () {
                    loggingEnabled$1 && logger$1.debug("get contexttype", this, this._contextType, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("contextType trace");
                    if (renderHookStep == 0) {
                        renderHookStep = 1;
                    }
                    if (this._contextType == null) {
                        this._contextType = {};
                    }
                    if (!this._contextType.appliedCurrentValueHook) {
                        logger$1.debug("applied currentvalue hook");
                        this._contextType.appliedCurrentValueHook = true;
                        Object.defineProperty(this._contextType, "_currentValue", {
                            configurable: true,
                            get: function () {
                                loggingEnabled$1 && logger$1.debug("get currentValue", this, stubsApplied, renderHookStep);
                                loggingEnabled$1 && console.trace("currentValue trace");
                                if (renderHookStep == 1) {
                                    renderHookStep = 2;
                                    applyStubsIfNeeded();
                                }
                                return this.__currentValue;
                            },
                            set: function (value) {
                                return this.__currentValue = value;
                            }
                        });
                    }
                    return this._contextType;
                },
                set: function (value) {
                    this._contextType = value;
                }
            });
            Object.defineProperty(component.prototype, "updater", {
                configurable: true,
                get: function () {
                    return this._updater;
                },
                set: function (value) {
                    loggingEnabled$1 && logger$1.debug("set updater", this, value, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("updater trace");
                    if (renderHookStep == 1 || renderHookStep == 2) {
                        renderHookStep = 0;
                        removeStubsIfNeeded();
                    }
                    return this._updater = value;
                }
            });
            Object.defineProperty(component, "getDerivedStateFromProps", {
                configurable: true,
                get: function () {
                    loggingEnabled$1 && logger$1.debug("get getDerivedStateFromProps", this, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("getDerivedStateFromProps trace");
                    if (renderHookStep == 1 || renderHookStep == 2) {
                        renderHookStep = 0;
                        removeStubsIfNeeded();
                    }
                    return this._getDerivedStateFromProps;
                },
                set: function (value) {
                    this._getDerivedStateFromProps = value;
                }
            });
        }
        else if (window.SP_REACTDOM.version.startsWith("18.")) {
            Object.defineProperty(component, "contextType", {
                configurable: true,
                get: function () {
                    loggingEnabled$1 && logger$1.debug("get contexttype", this, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("contextType trace");
                    if (renderHookStep == 0)
                        renderHookStep = 1;
                    else if (renderHookStep == 3)
                        renderHookStep = 4;
                    return this._contextType;
                },
                set: function (value) {
                    this._contextType = value;
                }
            });
            Object.defineProperty(component, "contextTypes", {
                configurable: true,
                get: function () {
                    loggingEnabled$1 && logger$1.debug("get contexttypes", this, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("contextTypes trace");
                    if (renderHookStep == 1) {
                        renderHookStep = 2;
                        applyStubsIfNeeded();
                    }
                    return this._contextTypes;
                },
                set: function (value) {
                    this._contextTypes = value;
                }
            });
            Object.defineProperty(component.prototype, "updater", {
                configurable: true,
                get: function () {
                    return this._updater;
                },
                set: function (value) {
                    loggingEnabled$1 && logger$1.debug("set updater", this, value, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("updater trace");
                    if (renderHookStep == 2) {
                        renderHookStep = 0;
                        removeStubsIfNeeded();
                    }
                    return this._updater = value;
                }
            });
            Object.defineProperty(component, "getDerivedStateFromProps", {
                configurable: true,
                get: function () {
                    loggingEnabled$1 && logger$1.debug("get getDerivedStateFromProps", this, stubsApplied, renderHookStep);
                    loggingEnabled$1 && console.trace("getDerivedStateFromProps trace");
                    if (renderHookStep == 2) {
                        renderHookStep = 0;
                        removeStubsIfNeeded();
                    }
                    return this._getDerivedStateFromProps;
                },
                set: function (value) {
                    this._getDerivedStateFromProps = value;
                }
            });
        }
        return userComponent;
    }

    let loggingEnabled = false;
    let perfLoggingEnabled = false;
    function setReactPatcherLoggingEnabled(value = true) { loggingEnabled = value; }
    function setReactPatcherPerformanceLoggingEnabled(value = true) { perfLoggingEnabled = value; }
    function patchComponent(node, handler, steps, step, caches, logger, prop = 'type') {
        loggingEnabled && logger.group('Patching node:', node);
        switch (typeof node?.[prop]) {
            case 'function':
                const patch = afterPatch(node, prop, steps[step + 1] ? createStepHandler(handler, steps, step + 1, caches, logger) : handler);
                loggingEnabled && logger.debug('Patched a function component', patch);
                break;
            case 'object':
                if (node[prop]?.prototype?.render) {
                    wrapReactClass(node);
                    const patch = afterPatch(node[prop].prototype, 'render', steps[step + 1] ? createStepHandler(handler, steps, step + 1, caches, logger) : handler);
                    loggingEnabled && logger.debug('Patched class component', patch);
                }
                else {
                    loggingEnabled && logger.debug('Patching forwardref/memo');
                    wrapReactType(node, prop);
                    patchComponent(node[prop], handler, steps, step, caches, logger, node[prop]?.render ? 'render' : 'type');
                }
                break;
            default:
                logger.error('Unhandled component type', node);
                break;
        }
        loggingEnabled && logger.groupEnd();
    }
    function handleStep(tree, handler, steps, step, caches, logger) {
        const startTime = (loggingEnabled || perfLoggingEnabled) ? performance.now() : 0;
        const stepHandler = steps[step];
        const cache = caches[step] || (caches[step] = new Map());
        loggingEnabled && logger.debug(`Patch step ${step} running`, { tree, stepHandler, step, caches });
        const node = stepHandler(tree);
        if (node && node.type) {
            loggingEnabled && logger.debug('Found node', node);
        }
        else if (node) {
            loggingEnabled && logger.error('Found node without type. Something is probably wrong.', node);
            return tree;
        }
        else {
            loggingEnabled && logger.warn('Found no node. Depending on your usecase, this might be fine.', node);
            return tree;
        }
        let cachedType;
        if (cachedType = cache.get(node.type)) {
            loggingEnabled && logger.debug('Found cached patched component', node);
            node.type = cachedType;
            (loggingEnabled || perfLoggingEnabled) && logger.debug(`Patch step ${step} took ${performance.now() - startTime}ms with cache`);
            return tree;
        }
        const originalType = node.type;
        patchComponent(node, handler, steps, step, caches, logger);
        cache.set(originalType, node.type);
        (loggingEnabled || perfLoggingEnabled) && logger.debug(`Patch step ${step} took ${performance.now() - startTime}ms`);
        return tree;
    }
    function createStepHandler(handler, steps, step, caches, logger) {
        loggingEnabled && logger.debug(`Creating handler for step ${step}`);
        return (_, tree) => handleStep(tree, handler, steps, step, caches, logger);
    }
    function createReactTreePatcher(steps, handler, debugName = 'ReactPatch') {
        const caches = [];
        const logger = new Logger(`ReactTreePatcher -> ${debugName}`);
        loggingEnabled && logger.debug('Init with options:', steps, debugName);
        return createStepHandler(handler, steps, 0, caches, logger);
    }

    function joinClassNames(...classes) {
        return classes.join(' ');
    }
    function sleep(ms) {
        return new Promise((res) => setTimeout(res, ms));
    }
    function findSP() {
        if (document.title == 'SP')
            return window;
        const navTrees = getGamepadNavigationTrees();
        return navTrees?.find((x) => x.m_ID == 'GamepadUI_Full_Root' || x.m_ID == 'root_1_')?.Root?.Element?.ownerDocument?.defaultView;
    }
    function getFocusNavController() {
        return window.GamepadNavTree?.m_context?.m_controller || window.FocusNavController;
    }
    function getGamepadNavigationTrees() {
        const focusNav = getFocusNavController();
        const context = focusNav?.m_ActiveContext || focusNav?.m_LastActiveContext;
        return context?.m_rgGamepadNavigationTrees;
    }

    const buttonItemRegex = createPropListRegex(["highlightOnFocus", "childrenContainerWidth"], false);
    const ButtonItem = Object.values(CommonUIModule).find((mod) => (mod?.render?.toString && buttonItemRegex.test(mod.render.toString())) ||
        mod?.render?.toString?.().includes('childrenContainerWidth:"min"'));

    const Carousel = findModuleExport((e) => e.render?.toString().includes('setFocusedColumn:'));

    const ControlsList = findModuleExport((e) => e?.toString && e.toString().includes('().ControlsListChild') && e.toString().includes('().ControlsListOuterPanel'));

    const DialogCheckbox = findModuleExport(e => e?.prototype &&
        typeof e?.prototype == "object" &&
        "GetPanelElementProps" in e?.prototype &&
        "SetChecked" in e?.prototype &&
        "Toggle" in e?.prototype &&
        (e?.prototype?.render?.toString?.().includes('="DialogCheckbox"') || (e.contextType &&
            e.prototype?.render?.toString?.().includes('fallback:'))));

    const Dropdown = Object.values(CommonUIModule).find((mod) => mod?.prototype?.SetSelectedOption && mod?.prototype?.BuildMenu);
    const dropdownItemRegex = createPropListRegex(["dropDownControlRef", "description"], false);
    const DropdownItemInternal = Object.values(CommonUIModule).find((mod) => mod?.toString && dropdownItemRegex.test(mod.toString()));
    const DropdownItem = ((args) => window.__PLAYHUB_HOST__.jsx.jsx(DropdownItemInternal, { childrenContainerWidth: "min", ...args }));

    const ErrorBoundary = findModuleExport((e) => e?.prototype?.Reset && e?.prototype?.componentDidCatch && e.prototype?.render?.toString().includes("lastErrorKey"));

    const Field = findModuleExport((e) => (e?.toString()?.includes('().Field') && e?.toString()?.includes('"shift-children-below"')) || e?.render?.toString()?.includes('"shift-children-below"'));

    const focusableRegex = createPropListRegex(["flow-children", "onActivate", "onCancel", "focusClassName", "focusWithinClassName"]);
    const Focusable = findModuleExport((e) => (typeof e == 'function' && e?.toString && focusableRegex.test(e.toString())) || (e?.render?.toString && focusableRegex.test(e.render.toString())));

    const FocusRing = findModuleExport((e) => e?.toString?.()?.includes('.GetShowDebugFocusRing())'));

    var GamepadButton;
    (function (GamepadButton) {
        GamepadButton[GamepadButton["INVALID"] = 0] = "INVALID";
        GamepadButton[GamepadButton["OK"] = 1] = "OK";
        GamepadButton[GamepadButton["CANCEL"] = 2] = "CANCEL";
        GamepadButton[GamepadButton["SECONDARY"] = 3] = "SECONDARY";
        GamepadButton[GamepadButton["OPTIONS"] = 4] = "OPTIONS";
        GamepadButton[GamepadButton["BUMPER_LEFT"] = 5] = "BUMPER_LEFT";
        GamepadButton[GamepadButton["BUMPER_RIGHT"] = 6] = "BUMPER_RIGHT";
        GamepadButton[GamepadButton["TRIGGER_LEFT"] = 7] = "TRIGGER_LEFT";
        GamepadButton[GamepadButton["TRIGGER_RIGHT"] = 8] = "TRIGGER_RIGHT";
        GamepadButton[GamepadButton["DIR_UP"] = 9] = "DIR_UP";
        GamepadButton[GamepadButton["DIR_DOWN"] = 10] = "DIR_DOWN";
        GamepadButton[GamepadButton["DIR_LEFT"] = 11] = "DIR_LEFT";
        GamepadButton[GamepadButton["DIR_RIGHT"] = 12] = "DIR_RIGHT";
        GamepadButton[GamepadButton["SELECT"] = 13] = "SELECT";
        GamepadButton[GamepadButton["START"] = 14] = "START";
        GamepadButton[GamepadButton["LSTICK_CLICK"] = 15] = "LSTICK_CLICK";
        GamepadButton[GamepadButton["RSTICK_CLICK"] = 16] = "RSTICK_CLICK";
        GamepadButton[GamepadButton["LSTICK_TOUCH"] = 17] = "LSTICK_TOUCH";
        GamepadButton[GamepadButton["RSTICK_TOUCH"] = 18] = "RSTICK_TOUCH";
        GamepadButton[GamepadButton["LPAD_TOUCH"] = 19] = "LPAD_TOUCH";
        GamepadButton[GamepadButton["LPAD_CLICK"] = 20] = "LPAD_CLICK";
        GamepadButton[GamepadButton["RPAD_TOUCH"] = 21] = "RPAD_TOUCH";
        GamepadButton[GamepadButton["RPAD_CLICK"] = 22] = "RPAD_CLICK";
        GamepadButton[GamepadButton["REAR_LEFT_UPPER"] = 23] = "REAR_LEFT_UPPER";
        GamepadButton[GamepadButton["REAR_LEFT_LOWER"] = 24] = "REAR_LEFT_LOWER";
        GamepadButton[GamepadButton["REAR_RIGHT_UPPER"] = 25] = "REAR_RIGHT_UPPER";
        GamepadButton[GamepadButton["REAR_RIGHT_LOWER"] = 26] = "REAR_RIGHT_LOWER";
        GamepadButton[GamepadButton["STEAM_GUIDE"] = 27] = "STEAM_GUIDE";
        GamepadButton[GamepadButton["STEAM_QUICK_MENU"] = 28] = "STEAM_QUICK_MENU";
    })(GamepadButton || (GamepadButton = {}));
    var NavEntryPositionPreferences;
    (function (NavEntryPositionPreferences) {
        NavEntryPositionPreferences[NavEntryPositionPreferences["FIRST"] = 0] = "FIRST";
        NavEntryPositionPreferences[NavEntryPositionPreferences["LAST"] = 1] = "LAST";
        NavEntryPositionPreferences[NavEntryPositionPreferences["MAINTAIN_X"] = 2] = "MAINTAIN_X";
        NavEntryPositionPreferences[NavEntryPositionPreferences["MAINTAIN_Y"] = 3] = "MAINTAIN_Y";
        NavEntryPositionPreferences[NavEntryPositionPreferences["PREFERRED_CHILD"] = 4] = "PREFERRED_CHILD";
    })(NavEntryPositionPreferences || (NavEntryPositionPreferences = {}));

    const Marquee = findModuleExport((e) => e?.toString && e.toString().includes('.Marquee') && e.toString().includes('--fade-length'));

    const showContextMenu = findModuleExport((e) => typeof e === 'function' &&
        e.toString().includes('GetContextMenuManagerFromWindow(') &&
        e.toString().includes('.CreateContextMenuInstance('));
    const MenuModule = findModuleDetailsByExport((e) => e?.render?.toString()?.includes('bPlayAudio:') || (e?.prototype?.OnOKButton && e?.prototype?.OnMouseEnter));
    const Menu = findModuleExport((e) => e?.prototype?.HideIfSubmenu && e?.prototype?.HideMenu) ||
        Object.values(MenuModule?.[0] ?? {}).find((e) => e?.toString()?.includes?.(`useId`) && e?.toString()?.includes?.(`labelId`));
    const MenuGoupModule = findModuleByExport(e => e?.prototype?.Focus && e?.prototype?.OnOKButton && e?.prototype?.render?.toString().includes?.(`"emphasis"==this.props.tone`));
    const MenuGroup = MenuGoupModule && Object.values(MenuGoupModule).find((e) => typeof e == "function" && e?.toString?.()?.includes("bInGamepadUI:"));
    const MenuItem = MenuModule?.[1];
    const MenuSeparator = findModuleExport((e) => typeof e === 'function' && /className:.+?\.ContextMenuSeparator/.test(e.toString()));

    const showModalRaw = findModuleExport((e) => typeof e === 'function' && e.toString().includes('props.bDisableBackgroundDismiss') && !e?.prototype?.Cancel);
    const showModal = (modal, parent, props = {
        strTitle: 'Decky Dialog',
        bHideMainWindowForPopouts: false,
    }) => {
        return showModalRaw(modal, parent || findSP() || window, props.strTitle, props, undefined, {
            bHideActions: props.bHideActionIcons,
        });
    };
    const ConfirmModal = findModuleExport((e) => e?.toString()?.includes('bUpdateDisabled') && e?.toString()?.includes('closeModal') && e?.toString()?.includes('onGamepadCancel'));
    const ModalRoot = findModuleExport((e) => typeof e === 'function' && e.toString().includes('Either closeModal or onCancel should be passed to GenericDialog. Classes: ')) ||
        Object.values(findModule((m) => {
            if (typeof m !== 'object')
                return false;
            for (let prop in m) {
                if (m[prop]?.m_mapModalManager && Object.values(m)?.find((x) => x?.type)) {
                    return true;
                }
            }
            return false;
        }) || {})?.find((x) => x?.type?.toString?.()?.includes('((function(){'));
    const [ModalModule, _ModalPosition] = findModuleDetailsByExport((e) => e?.toString().includes('.ModalPosition'), 5);
    const ModalModuleProps = ModalModule ? Object.values(ModalModule) : [];
    const SimpleModal = ModalModuleProps.find((prop) => {
        const string = prop?.toString();
        return string?.includes('.ShowPortalModal()') && string?.includes('.OnElementReadyCallbacks.Register(');
    });
    const ModalPosition = _ModalPosition;

    const [mod, panelSection] = findModuleDetailsByExport((e) => e.toString()?.includes('.PanelSection'));
    const PanelSection = panelSection;
    const PanelSectionRow = Object.values(mod).filter((exp) => !exp?.toString?.()?.includes('.PanelSection'))[0];

    const ProgressBar = findModuleExport((e) => e?.toString?.()?.includes('.ProgressBar,"standard"=='));
    const ProgressBarWithInfo = findModuleExport((e) => e?.toString?.()?.includes('.ProgressBarFieldStatus,children') || e?.toString?.()?.includes('.ProgressBarFieldStatus},'));
    const progressBarItemRegex = createPropListRegex(["indeterminate", "nTransitionSec", "nProgress"]);
    const ProgressBarItem = findModuleExport((e) => e?.toString && progressBarItemRegex.test(e.toString()));

    const sidebarNavigationRegex = createPropListRegex(["pages", "fnSetNavigateToPage", "disableRouteReporting"]);
    const SidebarNavigation = findModuleExport((e) => e?.toString && sidebarNavigationRegex.test(e.toString()));

    const SliderField = Object.values(CommonUIModule).find((mod) => mod?.toString?.()?.includes('SliderField,fallback') || mod?.toString?.()?.includes("SliderField\","));

    const Spinner = IconsModule && Object.values(IconsModule)?.find((mod) => mod?.toString && /Spinner\),children:\[\(0,\w+\.jsx\)\("path",\{d:"M18 /.test(mod.toString()) || /Spinner\)}\)?,.\.createElement\(\"path\",{d:\"M18 /.test(mod.toString()));

    const SteamSpinner = findModuleExport((e) => e?.toString?.()?.includes('Steam Spinner') && e?.toString?.()?.includes('src'));

    const tabsModule = findModuleByExport(e => e?.toString?.()?.includes(".TabRowTabs") && e?.toString?.()?.includes("activeTab:"));
    const Tabs = tabsModule && Object.values(tabsModule).find((e) => e?.type?.toString?.()?.includes("(function()"));

    const TextField = Object.values(CommonUIModule).find((mod) => mod?.validateUrl && mod?.validateEmail);

    const Toggle = Object.values(CommonUIModule).find((mod) => mod?.render?.toString?.()?.includes('.ToggleOff)'));

    const ToggleField = Object.values(CommonUIModule).find((mod) => mod?.render?.toString?.()?.includes('ToggleField,fallback') || mod?.render?.toString?.()?.includes("ToggleField\","));

    const ScrollingModule = findModuleByExport((e) => e?.render?.toString?.().includes('{case"x":'));
    const ScrollingModuleProps = ScrollingModule ? Object.values(ScrollingModule) : [];
    const ScrollPanel = ScrollingModuleProps.find((prop) => prop?.render?.toString?.().includes('{case"x":'));
    const ScrollPanelGroup = findModuleExport((e) => e?.render?.toString().includes('.FocusVisibleChild(),[])') || e?.render?.toString().includes('.FocusVisibleChild()),[])'));

    const SuspensefulImage = (props) => {
        const [loading, setLoading] = window.__PLAYHUB_HOST__.React.useState(true);
        const [error, setError] = window.__PLAYHUB_HOST__.React.useState(false);
        window.__PLAYHUB_HOST__.React.useEffect(() => {
            setLoading(true);
            setError(false);
            const img = new Image();
            img.src = props.src || '';
            img.addEventListener('load', () => {
                setLoading(false);
            });
            img.addEventListener('error', () => {
                setError(true);
            });
        }, [props.src]);
        return loading ? (window.__PLAYHUB_HOST__.jsx.jsx("div", { style: {
                width: props.suspenseWidth || props.style?.width,
                height: props.suspenseHeight || props.style?.height,
                background: 'rgba(255, 255, 255, 0.2)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
            }, children: error ? 'Missing image' : window.__PLAYHUB_HOST__.jsx.jsx(Spinner, { style: { height: '48px' } }) })) : (window.__PLAYHUB_HOST__.jsx.jsx("img", { ...props }));
    };

    const ColorPickerModal = ({ closeModal, onConfirm = () => { }, title = 'Color Picker', defaultH = 0, defaultS = 100, defaultL = 50, defaultA = 1, }) => {
        const [H, setH] = window.__PLAYHUB_HOST__.React.useState(defaultH);
        const [S, setS] = window.__PLAYHUB_HOST__.React.useState(defaultS);
        const [L, setL] = window.__PLAYHUB_HOST__.React.useState(defaultL);
        const [A, setA] = window.__PLAYHUB_HOST__.React.useState(defaultA);
        const colorPickerCSSVars = {
            '--decky-color-picker-hvalue': `${H}`,
            '--decky-color-picker-svalue': `${S}%`,
            '--decky-color-picker-lvalue': `${L}%`,
            '--decky-color-picker-avalue': `${A}`,
        };
        return (window.__PLAYHUB_HOST__.jsx.jsxs(ConfirmModal, { bAllowFullSize: true, onCancel: closeModal, onOK: () => {
                onConfirm(`hsla(${H}, ${S}%, ${L}%, ${A})`);
                closeModal();
            }, children: [window.__PLAYHUB_HOST__.jsx.jsx("style", { children: `
        /* This removes the cyan track color that is behind the slider head */
        .ColorPicker_Container .${gamepadSliderClasses.SliderTrack} {
          --left-track-color: #0000;
          /* This is for compatibility with the "Colored Toggles" CSSLoader Theme*/
          --colored-toggles-main-color: #0000;
        }

        .ColorPicker_HSlider .${gamepadSliderClasses.SliderTrack} {
          background: linear-gradient(
            270deg,
            hsla(360, var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue)),
            hsla(270, var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue)),
            hsla(180, var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue)),
            hsla(90, var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue)),
            hsla(0, var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue))
          );
        }

        .ColorPicker_SSlider .${gamepadSliderClasses.SliderTrack} {
          background: linear-gradient(
            90deg,
            hsla(var(--decky-color-picker-hvalue), 0%, var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue)),
            hsla(var(--decky-color-picker-hvalue), 100%, var(--decky-color-picker-lvalue), var(--decky-color-picker-avalue))
          );
        }

        .ColorPicker_LSlider .${gamepadSliderClasses.SliderTrack} {
          background: linear-gradient(
            90deg,
            hsla(var(--decky-color-picker-hvalue), var(--decky-color-picker-svalue), 0%, var(--decky-color-picker-avalue)),
            hsla(var(--decky-color-picker-hvalue), var(--decky-color-picker-svalue), 50%, var(--decky-color-picker-avalue)),
            hsla(var(--decky-color-picker-hvalue), var(--decky-color-picker-svalue), 100%, var(--decky-color-picker-avalue))
          );
        }

        .ColorPicker_ASlider .${gamepadSliderClasses.SliderTrack} {
          background: linear-gradient(
            90deg,
            hsla(var(--decky-color-picker-hvalue), var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), 0),
            hsla(var(--decky-color-picker-hvalue), var(--decky-color-picker-svalue), var(--decky-color-picker-lvalue), 1)
          );
        }
        ` }), window.__PLAYHUB_HOST__.jsx.jsxs("div", { className: "ColorPicker_ColorDisplayContainer", style: {
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        marginBottom: '1em',
                        marginTop: '-2.5em',
                    }, children: [window.__PLAYHUB_HOST__.jsx.jsx("div", { children: window.__PLAYHUB_HOST__.jsx.jsx("span", { style: { fontSize: '1.5em' }, children: window.__PLAYHUB_HOST__.jsx.jsx("b", { children: title }) }) }), window.__PLAYHUB_HOST__.jsx.jsx("div", { style: {
                                backgroundColor: `hsla(${H}, ${S}%, ${L}%, ${A})`,
                                width: '40px',
                                height: '40px',
                            } })] }), window.__PLAYHUB_HOST__.jsx.jsxs("div", { className: "ColorPicker_Container", style: colorPickerCSSVars, children: [window.__PLAYHUB_HOST__.jsx.jsx("div", { className: "ColorPicker_HSlider", children: window.__PLAYHUB_HOST__.jsx.jsx(SliderField, { showValue: true, editableValue: true, label: "Hue", value: H, min: 0, max: 360, onChange: setH }) }), window.__PLAYHUB_HOST__.jsx.jsx("div", { className: "ColorPicker_SSlider", children: window.__PLAYHUB_HOST__.jsx.jsx(SliderField, { showValue: true, editableValue: true, label: "Saturation", value: S, min: 0, max: 100, onChange: setS }) }), window.__PLAYHUB_HOST__.jsx.jsx("div", { className: "ColorPicker_LSlider", children: window.__PLAYHUB_HOST__.jsx.jsx(SliderField, { showValue: true, editableValue: true, label: "Lightness", value: L, min: 0, max: 100, onChange: setL }) }), window.__PLAYHUB_HOST__.jsx.jsx("div", { className: "ColorPicker_ASlider", children: window.__PLAYHUB_HOST__.jsx.jsx(SliderField, { showValue: true, editableValue: true, label: "Alpha", value: A, step: 0.1, min: 0, max: 1, onChange: setA }) })] })] }));
    };

    function ReorderableList(props) {
        if (props.animate === undefined)
            props.animate = true;
        const [entryList, setEntryList] = window.__PLAYHUB_HOST__.React.useState([...props.entries].sort((a, b) => a.position - b.position));
        const [reorderEnabled, setReorderEnabled] = window.__PLAYHUB_HOST__.React.useState(false);
        window.__PLAYHUB_HOST__.React.useEffect(() => {
            setEntryList([...props.entries].sort((a, b) => a.position - b.position));
        }, [props.entries]);
        window.__PLAYHUB_HOST__.React.useEffect(() => {
            if (props.disableReordering && reorderEnabled) {
                setReorderEnabled(false);
                props.onSave(entryList);
            }
        }, [props.disableReordering, reorderEnabled, entryList]);
        function toggleReorderEnabled() {
            if (props.disableReordering)
                return;
            let newReorderValue = !reorderEnabled;
            setReorderEnabled(newReorderValue);
            if (!newReorderValue) {
                props.onSave(entryList);
            }
        }
        function saveOnBackout(e) {
            const event = e;
            if (event.detail.button == GamepadButton.CANCEL && reorderEnabled) {
                setReorderEnabled(!reorderEnabled);
                props.onSave(entryList);
            }
        }
        return (window.__PLAYHUB_HOST__.jsx.jsx(window.__PLAYHUB_HOST__.React.Fragment, { children: window.__PLAYHUB_HOST__.jsx.jsx("div", { style: {
                    width: 'inherit',
                    height: 'inherit',
                    flex: '1 1 1px',
                    scrollPadding: '48px 0px',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'flex-start',
                    alignContent: 'stretch',
                }, children: window.__PLAYHUB_HOST__.jsx.jsx(Focusable, { onSecondaryButton: props.disableReordering ? undefined : toggleReorderEnabled, onSecondaryActionDescription: props.disableReordering ? undefined : reorderEnabled ? 'Save Order' : 'Reorder', onClick: props.disableReordering ? undefined : toggleReorderEnabled, onButtonDown: saveOnBackout, children: entryList.map((entry) => (window.__PLAYHUB_HOST__.jsx.jsx(ReorderableItem, { animate: props.animate, listData: entryList, entryData: entry, reorderEntryFunc: setEntryList, reorderEnabled: reorderEnabled, fieldProps: props.fieldProps, children: props.interactables ? window.__PLAYHUB_HOST__.jsx.jsx(props.interactables, { entry: entry }) : null }))) }) }) }));
    }
    function ReorderableItem(props) {
        const [isSelected, _setIsSelected] = window.__PLAYHUB_HOST__.React.useState(false);
        const [isSelectedLastFrame, setIsSelectedLastFrame] = window.__PLAYHUB_HOST__.React.useState(false);
        const listEntries = props.listData;
        function onReorder(e) {
            if (!props.reorderEnabled)
                return;
            const event = e;
            const currentIdx = listEntries.findIndex((entryData) => entryData === props.entryData);
            const currentIdxValue = listEntries[currentIdx];
            if (currentIdx < 0)
                return;
            let targetPosition = -1;
            if (event.detail.button == GamepadButton.DIR_DOWN) {
                targetPosition = currentIdxValue.position + 1;
            }
            else if (event.detail.button == GamepadButton.DIR_UP) {
                targetPosition = currentIdxValue.position - 1;
            }
            if (targetPosition >= listEntries.length || targetPosition < 0)
                return;
            let otherToUpdate = listEntries.find((entryData) => entryData.position === targetPosition);
            if (!otherToUpdate)
                return;
            let currentPosition = currentIdxValue.position;
            currentIdxValue.position = otherToUpdate.position;
            otherToUpdate.position = currentPosition;
            props.reorderEntryFunc([...listEntries].sort((a, b) => a.position - b.position));
        }
        async function setIsSelected(val) {
            _setIsSelected(val);
            for (let i = 0; i < 3; i++)
                await new Promise((res) => requestAnimationFrame(res));
            setIsSelectedLastFrame(val);
        }
        return (window.__PLAYHUB_HOST__.jsx.jsx("div", { style: props.animate
                ? {
                    transition: isSelected || isSelectedLastFrame
                        ? ''
                        : 'transform 0.3s cubic-bezier(0.25, 1, 0.5, 1), opacity 0.3s cubic-bezier(0.25, 1, 0.5, 1)',
                    transform: !props.reorderEnabled || isSelected ? 'scale(1)' : 'scale(0.9)',
                    opacity: !props.reorderEnabled || isSelected ? 1 : 0.7,
                }
                : {}, children: window.__PLAYHUB_HOST__.jsx.jsx(Field, { label: props.entryData.label, ...props.fieldProps, focusable: !props.children, onButtonDown: onReorder, onGamepadBlur: () => setIsSelected(false), onGamepadFocus: () => setIsSelected(true), children: window.__PLAYHUB_HOST__.jsx.jsx(Focusable, { style: { display: 'flex', width: '100%', position: 'relative' }, children: props.children }) }) }));
    }

    function getQuickAccessWindow() {
        const navTrees = getGamepadNavigationTrees();
        return (navTrees.find((tree) => tree?.id === 'QuickAccess-NA')?.m_Root?.m_element?.ownerDocument.defaultView ?? null);
    }
    function useQuickAccessVisible() {
        const [isHidden, setIsHidden] = window.__PLAYHUB_HOST__.React.useState(getQuickAccessWindow()?.document.hidden ?? false);
        window.__PLAYHUB_HOST__.React.useEffect(() => {
            const quickAccessWindow = getQuickAccessWindow();
            if (quickAccessWindow === null) {
                console.error('Could not get window of QuickAccess menu!');
                return;
            }
            const onVisibilityChange = () => setIsHidden(quickAccessWindow.document.hidden);
            quickAccessWindow.addEventListener('visibilitychange', onVisibilityChange);
            return () => {
                quickAccessWindow.removeEventListener('visibilitychange', onVisibilityChange);
            };
        }, []);
        return !isHidden;
    }

    const useParams = Object.values(ReactRouter).find((val) => /return (\w)\?\1\.params:{}/.test(`${val}`));

    var SideMenu;
    (function (SideMenu) {
        SideMenu[SideMenu["None"] = 0] = "None";
        SideMenu[SideMenu["Main"] = 1] = "Main";
        SideMenu[SideMenu["QuickAccess"] = 2] = "QuickAccess";
    })(SideMenu || (SideMenu = {}));
    var QuickAccessTab;
    (function (QuickAccessTab) {
        QuickAccessTab[QuickAccessTab["Notifications"] = 0] = "Notifications";
        QuickAccessTab[QuickAccessTab["RemotePlayTogetherControls"] = 1] = "RemotePlayTogetherControls";
        QuickAccessTab[QuickAccessTab["VoiceChat"] = 2] = "VoiceChat";
        QuickAccessTab[QuickAccessTab["Friends"] = 3] = "Friends";
        QuickAccessTab[QuickAccessTab["Settings"] = 4] = "Settings";
        QuickAccessTab[QuickAccessTab["Perf"] = 5] = "Perf";
        QuickAccessTab[QuickAccessTab["Help"] = 6] = "Help";
        QuickAccessTab[QuickAccessTab["Music"] = 7] = "Music";
        QuickAccessTab[QuickAccessTab["Decky"] = 999] = "Decky";
    })(QuickAccessTab || (QuickAccessTab = {}));
    const Router = findModuleExport((e) => e.Navigate && e.NavigationManager);
    let Navigation = {};
    const logger = new Logger("Navigation");
    try {
        function createNavigationFunction(fncName, handler) {
            return (...args) => {
                let win;
                try {
                    win = window.SteamUIStore.GetFocusedWindowInstance();
                }
                catch (e) {
                    logger.warn("Navigation interface failed to call GetFocusedWindowInstance", e);
                }
                if (!win) {
                    logger.warn("Navigation interface could not find any focused window. Falling back to Main Window Instance");
                    win = Router.WindowStore?.GamepadUIMainWindowInstance || Router?.WindowStore?.SteamUIWindows?.[0];
                }
                if (win) {
                    try {
                        const thisObj = handler && handler(win);
                        (thisObj || win)[fncName](...args);
                    }
                    catch (e) {
                        logger.error("Navigation handler failed", e);
                    }
                }
                else {
                    logger.error("Navigation interface could not find a window to navigate");
                }
            };
        }
        const newNavigation = {
            Navigate: createNavigationFunction("Navigate"),
            NavigateBack: createNavigationFunction("NavigateBack"),
            NavigateToAppProperties: createNavigationFunction("AppProperties", win => win.Navigator),
            NavigateToExternalWeb: createNavigationFunction("ExternalWeb", win => win.Navigator),
            NavigateToInvites: createNavigationFunction("Invites", win => win.Navigator),
            NavigateToChat: createNavigationFunction("Chat", win => win.Navigator),
            NavigateToLibraryTab: createNavigationFunction("LibraryTab", win => win.Navigator),
            NavigateToLayoutPreview: Router.NavigateToLayoutPreview?.bind(Router),
            NavigateToSteamWeb: createNavigationFunction("NavigateToSteamWeb"),
            OpenSideMenu: createNavigationFunction("OpenSideMenu", win => win.MenuStore),
            OpenQuickAccessMenu: createNavigationFunction("OpenQuickAccessMenu", win => win.MenuStore),
            OpenMainMenu: createNavigationFunction("OpenMainMenu", win => win.MenuStore),
            CloseSideMenus: createNavigationFunction("CloseSideMenus", win => win.MenuStore),
            OpenPowerMenu: Router.OpenPowerMenu?.bind(Router),
        };
        Object.assign(Navigation, newNavigation);
    }
    catch (e) {
        logger.error('Error initializing Navigation interface', e);
    }

    var EResult;
    (function (EResult) {
        EResult[EResult["OK"] = 1] = "OK";
        EResult[EResult["Fail"] = 2] = "Fail";
        EResult[EResult["NoConnection"] = 3] = "NoConnection";
        EResult[EResult["InvalidPassword"] = 5] = "InvalidPassword";
        EResult[EResult["LoggedInElsewhere"] = 6] = "LoggedInElsewhere";
        EResult[EResult["InvalidProtocolVer"] = 7] = "InvalidProtocolVer";
        EResult[EResult["InvalidParam"] = 8] = "InvalidParam";
        EResult[EResult["FileNotFound"] = 9] = "FileNotFound";
        EResult[EResult["Busy"] = 10] = "Busy";
        EResult[EResult["InvalidState"] = 11] = "InvalidState";
        EResult[EResult["InvalidName"] = 12] = "InvalidName";
        EResult[EResult["InvalidEmail"] = 13] = "InvalidEmail";
        EResult[EResult["DuplicateName"] = 14] = "DuplicateName";
        EResult[EResult["AccessDenied"] = 15] = "AccessDenied";
        EResult[EResult["Timeout"] = 16] = "Timeout";
        EResult[EResult["Banned"] = 17] = "Banned";
        EResult[EResult["AccountNotFound"] = 18] = "AccountNotFound";
        EResult[EResult["InvalidSteamID"] = 19] = "InvalidSteamID";
        EResult[EResult["ServiceUnavailable"] = 20] = "ServiceUnavailable";
        EResult[EResult["NotLoggedOn"] = 21] = "NotLoggedOn";
        EResult[EResult["Pending"] = 22] = "Pending";
        EResult[EResult["EncryptionFailure"] = 23] = "EncryptionFailure";
        EResult[EResult["InsufficientPrivilege"] = 24] = "InsufficientPrivilege";
        EResult[EResult["LimitExceeded"] = 25] = "LimitExceeded";
        EResult[EResult["Revoked"] = 26] = "Revoked";
        EResult[EResult["Expired"] = 27] = "Expired";
        EResult[EResult["AlreadyRedeemed"] = 28] = "AlreadyRedeemed";
        EResult[EResult["DuplicateRequest"] = 29] = "DuplicateRequest";
        EResult[EResult["AlreadyOwned"] = 30] = "AlreadyOwned";
        EResult[EResult["IPNotFound"] = 31] = "IPNotFound";
        EResult[EResult["PersistFailed"] = 32] = "PersistFailed";
        EResult[EResult["LockingFailed"] = 33] = "LockingFailed";
        EResult[EResult["LogonSessionReplaced"] = 34] = "LogonSessionReplaced";
        EResult[EResult["ConnectFailed"] = 35] = "ConnectFailed";
        EResult[EResult["HandshakeFailed"] = 36] = "HandshakeFailed";
        EResult[EResult["IOFailure"] = 37] = "IOFailure";
        EResult[EResult["RemoteDisconnect"] = 38] = "RemoteDisconnect";
        EResult[EResult["ShoppingCartNotFound"] = 39] = "ShoppingCartNotFound";
        EResult[EResult["Blocked"] = 40] = "Blocked";
        EResult[EResult["Ignored"] = 41] = "Ignored";
        EResult[EResult["NoMatch"] = 42] = "NoMatch";
        EResult[EResult["AccountDisabled"] = 43] = "AccountDisabled";
        EResult[EResult["ServiceReadOnly"] = 44] = "ServiceReadOnly";
        EResult[EResult["AccountNotFeatured"] = 45] = "AccountNotFeatured";
        EResult[EResult["AdministratorOK"] = 46] = "AdministratorOK";
        EResult[EResult["ContentVersion"] = 47] = "ContentVersion";
        EResult[EResult["TryAnotherCM"] = 48] = "TryAnotherCM";
        EResult[EResult["PasswordRequiredToKickSession"] = 49] = "PasswordRequiredToKickSession";
        EResult[EResult["AlreadyLoggedInElsewhere"] = 50] = "AlreadyLoggedInElsewhere";
        EResult[EResult["Suspended"] = 51] = "Suspended";
        EResult[EResult["Cancelled"] = 52] = "Cancelled";
        EResult[EResult["DataCorruption"] = 53] = "DataCorruption";
        EResult[EResult["DiskFull"] = 54] = "DiskFull";
        EResult[EResult["RemoteCallFailed"] = 55] = "RemoteCallFailed";
        EResult[EResult["PasswordUnset"] = 56] = "PasswordUnset";
        EResult[EResult["ExternalAccountUnlinked"] = 57] = "ExternalAccountUnlinked";
        EResult[EResult["PSNTicketInvalid"] = 58] = "PSNTicketInvalid";
        EResult[EResult["ExternalAccountAlreadyLinked"] = 59] = "ExternalAccountAlreadyLinked";
        EResult[EResult["RemoteFileConflict"] = 60] = "RemoteFileConflict";
        EResult[EResult["IllegalPassword"] = 61] = "IllegalPassword";
        EResult[EResult["SameAsPreviousValue"] = 62] = "SameAsPreviousValue";
        EResult[EResult["AccountLogonDenied"] = 63] = "AccountLogonDenied";
        EResult[EResult["CannotUseOldPassword"] = 64] = "CannotUseOldPassword";
        EResult[EResult["InvalidLoginAuthCode"] = 65] = "InvalidLoginAuthCode";
        EResult[EResult["AccountLogonDeniedNoMail"] = 66] = "AccountLogonDeniedNoMail";
        EResult[EResult["HardwareNotCapableOfIPT"] = 67] = "HardwareNotCapableOfIPT";
        EResult[EResult["IPTInitError"] = 68] = "IPTInitError";
        EResult[EResult["ParentalControlRestricted"] = 69] = "ParentalControlRestricted";
        EResult[EResult["FacebookQueryError"] = 70] = "FacebookQueryError";
        EResult[EResult["ExpiredLoginAuthCode"] = 71] = "ExpiredLoginAuthCode";
        EResult[EResult["IPLoginRestrictionFailed"] = 72] = "IPLoginRestrictionFailed";
        EResult[EResult["AccountLockedDown"] = 73] = "AccountLockedDown";
        EResult[EResult["AccountLogonDeniedVerifiedEmailRequired"] = 74] = "AccountLogonDeniedVerifiedEmailRequired";
        EResult[EResult["NoMatchingURL"] = 75] = "NoMatchingURL";
        EResult[EResult["BadResponse"] = 76] = "BadResponse";
        EResult[EResult["RequirePasswordReEntry"] = 77] = "RequirePasswordReEntry";
        EResult[EResult["ValueOutOfRange"] = 78] = "ValueOutOfRange";
        EResult[EResult["UnexpectedError"] = 79] = "UnexpectedError";
        EResult[EResult["Disabled"] = 80] = "Disabled";
        EResult[EResult["InvalidCEGSubmission"] = 81] = "InvalidCEGSubmission";
        EResult[EResult["RestrictedDevice"] = 82] = "RestrictedDevice";
        EResult[EResult["RegionLocked"] = 83] = "RegionLocked";
        EResult[EResult["RateLimitExceeded"] = 84] = "RateLimitExceeded";
        EResult[EResult["AccountLoginDeniedNeedTwoFactor"] = 85] = "AccountLoginDeniedNeedTwoFactor";
        EResult[EResult["ItemDeleted"] = 86] = "ItemDeleted";
        EResult[EResult["AccountLoginDeniedThrottle"] = 87] = "AccountLoginDeniedThrottle";
        EResult[EResult["TwoFactorCodeMismatch"] = 88] = "TwoFactorCodeMismatch";
        EResult[EResult["TwoFactorActivationCodeMismatch"] = 89] = "TwoFactorActivationCodeMismatch";
        EResult[EResult["AccountAssociatedToMultiplePartners"] = 90] = "AccountAssociatedToMultiplePartners";
        EResult[EResult["NotModified"] = 91] = "NotModified";
        EResult[EResult["NoMobileDevice"] = 92] = "NoMobileDevice";
        EResult[EResult["TimeNotSynced"] = 93] = "TimeNotSynced";
        EResult[EResult["SmsCodeFailed"] = 94] = "SmsCodeFailed";
        EResult[EResult["AccountLimitExceeded"] = 95] = "AccountLimitExceeded";
        EResult[EResult["AccountActivityLimitExceeded"] = 96] = "AccountActivityLimitExceeded";
        EResult[EResult["PhoneActivityLimitExceeded"] = 97] = "PhoneActivityLimitExceeded";
        EResult[EResult["RefundToWallet"] = 98] = "RefundToWallet";
        EResult[EResult["EmailSendFailure"] = 99] = "EmailSendFailure";
        EResult[EResult["NotSettled"] = 100] = "NotSettled";
        EResult[EResult["NeedCaptcha"] = 101] = "NeedCaptcha";
        EResult[EResult["GSLTDenied"] = 102] = "GSLTDenied";
        EResult[EResult["GSOwnerDenied"] = 103] = "GSOwnerDenied";
        EResult[EResult["InvalidItemType"] = 104] = "InvalidItemType";
        EResult[EResult["IPBanned"] = 105] = "IPBanned";
        EResult[EResult["GSLTExpired"] = 106] = "GSLTExpired";
        EResult[EResult["InsufficientFunds"] = 107] = "InsufficientFunds";
        EResult[EResult["TooManyPending"] = 108] = "TooManyPending";
        EResult[EResult["NoSiteLicensesFound"] = 109] = "NoSiteLicensesFound";
        EResult[EResult["WGNetworkSendExceeded"] = 110] = "WGNetworkSendExceeded";
        EResult[EResult["AccountNotFriends"] = 111] = "AccountNotFriends";
        EResult[EResult["LimitedUserAccount"] = 112] = "LimitedUserAccount";
    })(EResult || (EResult = {}));
    var EBrowserType;
    (function (EBrowserType) {
        EBrowserType[EBrowserType["OffScreen"] = 0] = "OffScreen";
        EBrowserType[EBrowserType["OpenVROverlay"] = 1] = "OpenVROverlay";
        EBrowserType[EBrowserType["OpenVROverlay_Dashboard"] = 2] = "OpenVROverlay_Dashboard";
        EBrowserType[EBrowserType["DirectHWND"] = 3] = "DirectHWND";
        EBrowserType[EBrowserType["DirectHWND_Borderless"] = 4] = "DirectHWND_Borderless";
        EBrowserType[EBrowserType["DirectHWND_Hidden"] = 5] = "DirectHWND_Hidden";
        EBrowserType[EBrowserType["ChildHWNDNative"] = 6] = "ChildHWNDNative";
        EBrowserType[EBrowserType["Offscreen_SteamUI"] = 12] = "Offscreen_SteamUI";
        EBrowserType[EBrowserType["OpenVROverlay_Subview"] = 13] = "OpenVROverlay_Subview";
    })(EBrowserType || (EBrowserType = {}));
    var ESteamRealm;
    (function (ESteamRealm) {
        ESteamRealm[ESteamRealm["Unknown"] = 0] = "Unknown";
        ESteamRealm[ESteamRealm["Global"] = 1] = "Global";
        ESteamRealm[ESteamRealm["China"] = 2] = "China";
    })(ESteamRealm || (ESteamRealm = {}));
    var EUIComposition;
    (function (EUIComposition) {
        EUIComposition[EUIComposition["Hidden"] = 0] = "Hidden";
        EUIComposition[EUIComposition["Notification"] = 1] = "Notification";
        EUIComposition[EUIComposition["Overlay"] = 2] = "Overlay";
        EUIComposition[EUIComposition["Opaque"] = 3] = "Opaque";
        EUIComposition[EUIComposition["OverlayKeyboard"] = 4] = "OverlayKeyboard";
    })(EUIComposition || (EUIComposition = {}));
    var EUIMode;
    (function (EUIMode) {
        EUIMode[EUIMode["Unknown"] = -1] = "Unknown";
        EUIMode[EUIMode["GamePad"] = 4] = "GamePad";
        EUIMode[EUIMode["Desktop"] = 7] = "Desktop";
    })(EUIMode || (EUIMode = {}));

    const definePlugin = (fn) => {
        return (...args) => {
            return fn(...args);
        };
    };

    var DeckyUi = /*#__PURE__*/Object.freeze({
        __proto__: null,
        Button: Button,
        ButtonItem: ButtonItem,
        Carousel: Carousel,
        ColorPickerModal: ColorPickerModal,
        CommonUIModule: CommonUIModule,
        ConfirmModal: ConfirmModal,
        ControlsList: ControlsList,
        DialogBody: DialogBody,
        DialogBodyText: DialogBodyText,
        DialogButton: DialogButton,
        DialogButtonPrimary: DialogButtonPrimary,
        DialogButtonSecondary: DialogButtonSecondary,
        DialogCheckbox: DialogCheckbox,
        DialogControlsSection: DialogControlsSection,
        DialogControlsSectionHeader: DialogControlsSectionHeader,
        DialogFooter: DialogFooter,
        DialogHeader: DialogHeader,
        DialogLabel: DialogLabel,
        DialogSubHeader: DialogSubHeader,
        Dropdown: Dropdown,
        DropdownItem: DropdownItem,
        DropdownItemInternal: DropdownItemInternal,
        get EBrowserType () { return EBrowserType; },
        get EResult () { return EResult; },
        get ESteamRealm () { return ESteamRealm; },
        get EUIComposition () { return EUIComposition; },
        get EUIMode () { return EUIMode; },
        ErrorBoundary: ErrorBoundary,
        Field: Field,
        FocusRing: FocusRing,
        Focusable: Focusable,
        get GamepadButton () { return GamepadButton; },
        INTERNAL_HOOKS: INTERNAL_HOOKS,
        IconsModule: IconsModule,
        Marquee: Marquee,
        Menu: Menu,
        MenuGroup: MenuGroup,
        MenuItem: MenuItem,
        MenuSeparator: MenuSeparator,
        ModalPosition: ModalPosition,
        ModalRoot: ModalRoot,
        get NavEntryPositionPreferences () { return NavEntryPositionPreferences; },
        Navigation: Navigation,
        PanelSection: PanelSection,
        PanelSectionRow: PanelSectionRow,
        ProgressBar: ProgressBar,
        ProgressBarItem: ProgressBarItem,
        ProgressBarWithInfo: ProgressBarWithInfo,
        get QuickAccessTab () { return QuickAccessTab; },
        ReactRouter: ReactRouter,
        ReorderableList: ReorderableList,
        Router: Router,
        ScrollPanel: ScrollPanel,
        ScrollPanelGroup: ScrollPanelGroup,
        get SideMenu () { return SideMenu; },
        SidebarNavigation: SidebarNavigation,
        SimpleModal: SimpleModal,
        SliderField: SliderField,
        Spinner: Spinner,
        SteamSpinner: SteamSpinner,
        SuspensefulImage: SuspensefulImage,
        Tabs: Tabs,
        TextField: TextField,
        Toggle: Toggle,
        ToggleField: ToggleField,
        achievementClasses: achievementClasses,
        achievementListClasses: achievementListClasses,
        achievementPageClasses: achievementPageClasses,
        afterPatch: afterPatch,
        appActionButtonClasses: appActionButtonClasses,
        appDetailsClasses: appDetailsClasses,
        appDetailsHeaderClasses: appDetailsHeaderClasses,
        applyHookStubs: applyHookStubs,
        basicAppDetailsSectionStylerClasses: basicAppDetailsSectionStylerClasses,
        beforePatch: beforePatch,
        callOriginal: callOriginal,
        classMap: classMap,
        classModuleMap: classModuleMap,
        createModuleMapping: createModuleMapping,
        createPropListRegex: createPropListRegex,
        createReactTreePatcher: createReactTreePatcher,
        definePlugin: definePlugin,
        fakeRenderComponent: fakeRenderComponent,
        findAllModules: findAllModules,
        findClass: findClass,
        findClassByName: findClassByName,
        findClassModule: findClassModule,
        findInReactTree: findInReactTree,
        findInTree: findInTree,
        findModule: findModule,
        findModuleByExport: findModuleByExport,
        findModuleChild: findModuleChild,
        findModuleDetailsByExport: findModuleDetailsByExport,
        findModuleExport: findModuleExport,
        findSP: findSP,
        focusRingClasses: focusRingClasses,
        footerClasses: footerClasses,
        gamepadContextMenuClasses: gamepadContextMenuClasses,
        gamepadDialogClasses: gamepadDialogClasses,
        gamepadLibraryClasses: gamepadLibraryClasses,
        gamepadSliderClasses: gamepadSliderClasses,
        gamepadTabbedPageClasses: gamepadTabbedPageClasses,
        gamepadUIClasses: gamepadUIClasses,
        getFocusNavController: getFocusNavController,
        getGamepadNavigationTrees: getGamepadNavigationTrees,
        getParentWindow: getParentWindow,
        getReactInstance: getReactInstance,
        getReactRoot: getReactRoot,
        injectFCTrampoline: injectFCTrampoline,
        joinClassNames: joinClassNames,
        libraryAssetImageClasses: libraryAssetImageClasses,
        mainBrowserClasses: mainBrowserClasses,
        mainMenuAppRunningClasses: mainMenuAppRunningClasses,
        modules: modules,
        playSectionClasses: playSectionClasses,
        quickAccessControlsClasses: quickAccessControlsClasses,
        quickAccessMenuClasses: quickAccessMenuClasses,
        removeHookStubs: removeHookStubs,
        replacePatch: replacePatch,
        scrollClasses: scrollClasses,
        scrollPanelClasses: scrollPanelClasses,
        searchBarClasses: searchBarClasses,
        setFCTrampolineLoggingEnabled: setFCTrampolineLoggingEnabled,
        setReactPatcherLoggingEnabled: setReactPatcherLoggingEnabled,
        setReactPatcherPerformanceLoggingEnabled: setReactPatcherPerformanceLoggingEnabled,
        showContextMenu: showContextMenu,
        showModal: showModal,
        sleep: sleep,
        staticClasses: staticClasses,
        steamSpinnerClasses: steamSpinnerClasses,
        unminifyClass: unminifyClass,
        updaterFieldClasses: updaterFieldClasses,
        useParams: useParams,
        useQuickAccessVisible: useQuickAccessVisible,
        useWindowRef: useWindowRef,
        wrapReactClass: wrapReactClass,
        wrapReactType: wrapReactType
    });

    // Generated by tools/steam-ui-contract.mjs from the real plugin sources.
    // Do not edit by hand: tests/steam-ui-adapter.test.mjs regenerates and compares it.
    const REQUIRED_STEAM_UI = [
        'ButtonItem',
        'ConfirmModal',
        'DialogButton',
        'DropdownItem',
        'Focusable',
        'GamepadButton',
        'ModalRoot',
        'Navigation',
        'PanelSection',
        'PanelSectionRow',
        'Router',
        'ScrollPanel',
        'SliderField',
        'SteamSpinner',
        'Tabs',
        'TextField',
        'ToggleField',
        'afterPatch',
        'createReactTreePatcher',
        'findInReactTree',
        'findModuleByExport',
        'getReactRoot',
        'showModal',
        'staticClasses',
    ];

    // Verified Steam UI adapter.
    //
    // Steam's UI components are resolved by @decky/ui (LGPL-2.1, see
    // vendor/DeckyUI-LICENSE), the same resolver Decky itself uses, instead of the
    // heuristic source matching of the first trial: that matching picked the wrong
    // native dropdown (the country field showed "Steam Beta Update").
    // The adapter only reads Steam's own webpack chunk. It never starts Decky, and
    // it never touches loader globals or another plugin's state.
    function missingSteamUi(ui) {
        return REQUIRED_STEAM_UI.filter((name) => {
            const value = ui?.[name];
            return value === undefined || value === null;
        });
    }
    function verifySteamUi(ui) {
        const missing = missingSteamUi(ui);
        if (missing.length)
            throw new Error('Steam UI components unavailable: ' + missing.join(', '));
        return ui;
    }
    const DFL = verifySteamUi(DeckyUi);
    const host$2 = window.__PLAYHUB_HOST__;
    if (host$2) {
        host$2.DFL = DFL;
        host$2.qamAvailable = true;
        host$2.diagnostics = { ...(host$2.diagnostics ?? {}), uiResolver: '@decky/ui', components: REQUIRED_STEAM_UI.length };
    }

    // Pure helpers for the standalone host: route injection, route patches and the
    // Quick Access Menu tab. No Steam, React or DOM access, so they run under
    // `node --test` exactly as they run in the renderer.
    //
    // The technique (wrap the router's output, append a cached keyed route array,
    // insert one stable tab object into the QAM tab list) follows ShelvesHub
    // d237922 (Jonathan Santos, MIT — vendor/ShelvesHub-LICENSE). The code here is
    // Playhub's own and carries no ShelvesHub identity, globals or behaviour.

    const OUR_ROUTES = Symbol.for('playhub.standalone.routes.v1');
    const PATCHED = Symbol.for('playhub.standalone.route-patched.v1');

    /** Build our route elements once per (route set, Route type) and keep identities stable. */
    function createRouteBuilder(React) {
      let built = null;
      let signature = '';
      let builtType = null;
      return function buildRoutes(Route, routes, Boundary) {
        const next = [...routes.keys()].sort().join('|');
        if (built && signature === next && builtType === Route) return built;
        const list = [];
        list[OUR_ROUTES] = true;
        for (const [path, entry] of routes) {
          const page = React.createElement(entry.component);
          const body = Boundary ? React.createElement(Boundary, null, page) : page;
          list.push(React.createElement(Route, { key: 'playhub-route:' + path, path, ...(entry.props || {}) }, body));
        }
        built = list;
        signature = next;
        builtType = Route;
        return list;
      };
    }

    /** The route element type Steam itself uses, taken from the list we are about to extend. */
    function routeTypeFromList(routeList) {
      for (const entry of routeList || []) {
        const candidates = Array.isArray(entry) ? entry : [entry];
        for (const element of candidates) {
          if (element && element.props && element.props.path !== undefined && element.type) return element.type;
        }
      }
      return undefined;
    }

    /**
     * Append our routes at the END of Steam's main route list, reusing our own slot
     * so repeated renders never duplicate them. Steam's routes keep their position,
     * so React reconciles them in place instead of remounting.
     */
    function injectRoutes(routeList, ourRoutes) {
      if (!Array.isArray(routeList) || !ourRoutes || !ourRoutes.length) return false;
      const last = routeList.length ? routeList[routeList.length - 1] : null;
      if (last && last[OUR_ROUTES]) routeList[routeList.length - 1] = ourRoutes;
      else routeList.push(ourRoutes);
      return true;
    }

    /** Apply registered route patches once per rendered route. */
    function applyRoutePatches(routeList, patches, onError) {
      if (!Array.isArray(routeList) || !patches || !patches.size) return;
      for (const entry of routeList) {
        const elements = Array.isArray(entry) ? entry : [entry];
        for (const route of elements) {
          const path = route && route.props && route.props.path;
          const set = path === undefined ? undefined : patches.get(String(path));
          if (!set || !set.size) continue;
          if (route.props.children && route.props.children[PATCHED]) continue;
          for (const patch of set) {
            try {
              const result = patch({ ...route.props });
              if (result && result.children !== undefined) route.props.children = result.children;
            } catch (error) { onError?.(path, error); }
          }
          try { if (route.props.children) route.props.children[PATCHED] = true; } catch { /* frozen children */ }
        }
      }
    }

    /**
     * Insert our tab once, after `afterKey` when that tab exists. Steam rebuilds the
     * list on every render, so the same tab object is reused: a new object per render
     * remounts the panel and loses gamepad focus.
     */
    function insertQamTab(tabs, tab, afterKey, visible) {
      if (!Array.isArray(tabs) || !tab) return 'skipped';
      for (const existing of tabs) {
        if (existing && existing.key === tab.key) {
          if (typeof existing.qAMVisibilitySetter === 'function') existing.qAMVisibilitySetter(visible);
          else existing.initialVisibility = visible;
          return 'present';
        }
      }
      tab.initialVisibility = visible;
      let at = tabs.length;
      {
        const index = tabs.findIndex((entry) => entry && entry.key === afterKey);
        if (index >= 0) at = index + 1;
      }
      tabs.splice(at, 0, tab);
      return 'inserted';
    }

    /** Register our numeric key in Steam's QuickAccessTab enum, so the panel gets a real class. */
    function registerTabKey(tabEnum, key, name) {
      if (!tabEnum || typeof tabEnum !== 'object') return false;
      if (tabEnum[key] !== undefined) return tabEnum[key] === name;
      tabEnum[key] = name;
      tabEnum[name] = key;
      return true;
    }

    // React 19.1.1 conserva la funzione risolta nel SimpleMemoComponent montato.
    // Questa integrazione privata resta deliberatamente vincolata alla build
    // verificata: su altre versioni si attende un render naturale, senza indovinare.
    const VERIFIED_REACT_VERSION = '19.1.1';
    const SYNC_LANE = 2;
    let sequence = 0;

    function walkFibers(root, visit, limit = 50000) {
      if (!root) return;
      const stack = [root];
      const seen = new Set();
      while (stack.length && seen.size < limit) {
        const node = stack.pop();
        if (!node || seen.has(node)) continue;
        seen.add(node);
        visit(node);
        if (node.sibling) stack.push(node.sibling);
        if (node.child) stack.push(node.child);
      }
    }

    function currentRoot(value) {
      let node = value?._internalRoot?.current || value?.current || value;
      const seen = new Set();
      while (node?.return && !seen.has(node)) { seen.add(node); node = node.return; }
      return node?.tag === 3 ? (node.stateNode?.current || node) : null;
    }

    // Non si aggiungono hook alla funzione Steam: cambiarne l'ordine corromperebbe
    // lo stato gia' montato. Si usa una vera ForceUpdate di una classe esistente
    // per schedulare la root, e la stessa lane per rendere raggiungibile il ramo.
    function refreshFiber(fiber, ReactDOM, reactVersion) {
      const no = reason => ({ scheduled: false, reason });
      if (reactVersion !== VERIFIED_REACT_VERSION) return no('unsupported-react-version');
      if (typeof ReactDOM?.flushSync !== 'function') return no('flushSync-unavailable');
      if (!fiber || ![0, 1, 11, 15].includes(fiber.tag)) return no('unsupported-fiber-tag');
      if (!fiber.memoizedProps || typeof fiber.memoizedProps !== 'object') return no('props-unavailable');
      const root = currentRoot(fiber);
      if (!root || root.stateNode?.tag !== 1) return no('concurrent-root-unavailable');
      let found = false;
      walkFibers(root, node => { if (node === fiber) found = true; });
      if (!found) return no('stale-fiber');
      let ancestor = null;
      let ancestorHops = 0;
      let hops = 0;
      for (let node = fiber; node; node = node.return, hops++) {
        // Un menu nascosto non deve essere aperto come effetto collaterale. Il
        // monitor riprova quando Steam rende visibile il ramo Offscreen/Suspense.
        if ((node.tag === 22 && node.memoizedState !== null) ||
            (node.tag === 13 && node.memoizedState !== null)) return no('hidden-or-suspended');
        if (!ancestor && node.tag === 1 &&
            typeof node.stateNode?.updater?.enqueueForceUpdate === 'function') {
          ancestor = node; ancestorHops = hops;
        }
      }
      if (!ancestor) return no('class-updater-unavailable');
      const oldProps = fiber.memoizedProps;
      const marker = '__playhubRefresh_' + (++sequence);
      const stamped = { ...oldProps, [marker]: sequence };
      let scheduled = false;
      try {
        ReactDOM.flushSync(() => {
          // flushSync assegna SyncLane alla ForceUpdate e React schedula davvero
          // la root. Scrivere soltanto fiber.lanes non farebbe partire alcun lavoro.
          ancestor.stateNode.updater.enqueueForceUpdate(ancestor.stateNode);
          const lanes = (ancestor.lanes || 0) | (ancestor.alternate?.lanes || 0);
          if (!(lanes & SYNC_LANE)) return;
          // Cambiamo SOLO il ricordo delle props, non quelle passate alla funzione.
          // Senza questa invalidazione il render puo' avvenire, ma il suo risultato
          // essere scartato da updateFunctionComponent per didReceiveUpdate=false.
          fiber.memoizedProps = stamped;
          fiber.lanes |= SYNC_LANE;
          if (fiber.alternate) fiber.alternate.lanes |= SYNC_LANE;
          for (let node = fiber.return; node; node = node.return) {
            node.childLanes |= SYNC_LANE;
            if (node.alternate) node.alternate.childLanes |= SYNC_LANE;
          }
          scheduled = true;
        });
        return { scheduled, reason: scheduled ? 'sync-lane-scheduled' : 'sync-lane-not-observed', ancestorHops };
      } catch (error) {
        return { scheduled, reason: 'refresh-error', error: String(error), ancestorHops };
      } finally {
        // Dopo il commit l'alternate puo' conservare il vecchio oggetto; non
        // lasciamo props artificiali nelle copie inattive o dopo un'eccezione.
        for (const node of [fiber, fiber.alternate])
          if (node?.memoizedProps === stamped) node.memoizedProps = oldProps;
      }
    }

    // Si rimuove per identita', mai soltanto per chiave: una scheda di Decky puo'
    // avere la stessa chiave numerica e non appartiene a questo motore.
    function removeOwnedTab(tabs, tab) {
      if (!Array.isArray(tabs)) return 0;
      let removed = 0;
      for (let i = tabs.length - 1; i >= 0; i--)
        if (tabs[i] === tab) { tabs.splice(i, 1); removed++; }
      return removed;
    }

    // I nuovi array evitano sia props congelate sia cache basate sull'identita'
    // dell'array tabs. cloneElement conserva key/ref e non aggiunge UI o wrapper.
    function mapRenderedTabs(output, transform, React, depth = 0) {
      if (depth > 60 || output == null) return output;
      if (Array.isArray(output)) {
        let changed = false;
        const next = output.map(value => {
          const result = mapRenderedTabs(value, transform, React, depth + 1);
          changed ||= result !== value;
          return result;
        });
        return changed ? next : output;
      }
      if (!React.isValidElement(output)) return output;
      const props = output.props;
      const patch = {};
      let changed = false;
      if (Array.isArray(props.tabs)) {
        const tabs = transform(props.tabs);
        if (tabs !== props.tabs) { patch.tabs = tabs; changed = true; }
      }
      if (props.children !== undefined) {
        const children = mapRenderedTabs(props.children, transform, React, depth + 1);
        if (children !== props.children) { patch.children = children; changed = true; }
      }
      return changed ? React.cloneElement(output, patch) : output;
    }

    // Il plugin resta quello originale: qui si collegano soltanto rotte e QAM.
    // La tecnica delle liste deriva da ShelvesHub d237922 (MIT, vendor/ShelvesHub-LICENSE).
    const host$1 = window.__PLAYHUB_HOST__;
    const React = host$1?.React;
    const ui = DFL;
    const source = (value) => { try {
        return String(value?.render || value);
    }
    catch {
        return '';
    } };
    const warn = (...args) => console.warn('[Playhub standalone]', ...args);
    const DECKY_API = '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
    const PLAYHUB_TAB_KEY = 0x50484B;
    const TAB_NAME = 'Playhub';
    const TAB_AFTER = 5;
    function allowed() {
        try {
            return !window[DECKY_API] && !host$1?.hasForeignRenderer?.();
        }
        catch {
            return false;
        }
    }
    // Il QAM puo' vivere in una finestra diversa dalla principale. Si cercano solo
    // root di React, mai istanze alternate o nodi ricostruiti da un vecchio render.
    function roots() {
        const docs = new Set([document, host$1?.targetDocument, ...(host$1?.targetDocuments || [])]);
        const add = (instance) => {
            for (const value of [instance, instance?.window, instance?.m_Window, instance?.m_popup,
                instance?.m_BrowserWindow, instance?.BrowserWindow]) {
                try {
                    if (value?.document?.body)
                        docs.add(value.document);
                }
                catch { /* finestra chiusa */ }
            }
        };
        try {
            const store = window.SteamUIStore?.WindowStore;
            add(store?.GamepadUIMainWindowInstance);
            for (const value of store?.SteamUIWindows || [])
                add(value);
            for (const value of window.g_PopupManager?.GetPopups?.() || [])
                add(value);
        }
        catch { /* Steam sta ricreando le finestre */ }
        const result = new Set();
        for (const doc of docs) {
            if (!doc)
                continue;
            let elements = [];
            try {
                elements = [doc.getElementById('root'), doc.body, doc.documentElement, ...Array.from(doc.body?.children || []).slice(0, 32)];
            }
            catch {
                continue;
            }
            for (const element of elements) {
                if (!element)
                    continue;
                try {
                    const root = currentRoot(ui.getReactRoot?.(element));
                    if (root)
                        result.add(root);
                }
                catch { /* adattatore non disponibile qui */ }
                for (const key of Object.keys(element)) {
                    if (!key.startsWith('__reactContainer$') && !key.startsWith('__reactFiber$'))
                        continue;
                    const root = currentRoot(element[key]);
                    if (root)
                        result.add(root);
                }
            }
        }
        return [...result];
    }
    function fibers(match) {
        const result = new Set();
        for (const root of roots())
            walkFibers(root, (node) => { if (match(node))
                result.add(node); });
        return [...result];
    }
    function renderWrapper(inner, after) {
        const wrapped = function (...args) {
            return after(inner.apply(this, args));
        };
        try {
            Object.assign(wrapped, inner);
            wrapped.toString = () => inner.toString();
        }
        catch { /* funzione non estensibile */ }
        return wrapped;
    }
    function replaceLiveType(node, original, replacement) {
        // SimpleMemoComponent usa fiber.type, non rilegge memo.type. Si aggiornano
        // entrambe le copie, ma solo se non sono state sostituite da qualcun altro.
        for (const copy of [node, node?.alternate])
            if (copy && copy.type === original && [0, 15].includes(copy.tag))
                copy.type = replacement;
    }
    function installRouterHook() {
        const routes = new Map();
        const patches = new Map();
        const globals = new Map();
        const listeners = new Set();
        const routeLists = new Set();
        const buildRoutes = createRouteBuilder(React);
        let wrapperMounted = false;
        let stopped = false;
        let patch = null;
        let timer;
        let attempts = 0;
        const tried = new WeakSet();
        const bump = () => listeners.forEach(listener => { try {
            listener();
        }
        catch { /* componente smontato */ } });
        const subscribe = (listener) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
        const removeSlots = (list) => {
            for (let i = list.length - 1; i >= 0; i--)
                if (list[i]?.[OUR_ROUTES])
                    list.splice(i, 1);
        };
        function process(output) {
            if (!output?.props)
                return;
            const top = output.props.children;
            const containers = Array.isArray(top) ? top : [top];
            const lists = [];
            for (const container of containers)
                if (Array.isArray(container?.props?.children))
                    lists.push(container.props.children);
            for (const list of lists) {
                if (stopped || !allowed()) {
                    removeSlots(list);
                    continue;
                }
                applyRoutePatches(list, patches, (path, error) => warn('route patch', path, error));
            }
            if (stopped || !allowed() || !lists.length)
                return;
            if (!routes.size) {
                removeSlots(lists[0]);
                return;
            }
            const Route = routeTypeFromList(lists[0]);
            if (!Route)
                return;
            injectRoutes(lists[0], buildRoutes(Route, routes, undefined));
            routeLists.add(lists[0]);
            if (routeLists.size > 64)
                routeLists.delete(routeLists.values().next().value);
        }
        function RouteWrapper(props) {
            const [, set] = React.useState(0);
            React.useEffect(() => subscribe(() => set((value) => (value + 1) | 0)), []);
            try {
                process(props.children);
            }
            catch (error) {
                warn('router wrapper', error);
            }
            return props.children;
        }
        function GlobalWrapper() {
            const [, set] = React.useState(0);
            React.useEffect(() => subscribe(() => set((value) => (value + 1) | 0)), []);
            if (stopped || !allowed() || !globals.size)
                return null;
            const extras = [];
            globals.forEach((component, id) => extras.push(React.createElement(component, { key: 'playhub-global:' + id })));
            return React.createElement(React.Fragment, null, extras);
        }
        function after(rendered) {
            if (stopped || !allowed() || !rendered?.props)
                return rendered;
            wrapperMounted = true;
            return React.createElement(React.Fragment, { key: 'playhub-router-root' }, React.createElement(RouteWrapper, { key: 'playhub-router' }, rendered), React.createElement(GlobalWrapper, { key: 'playhub-globals' }));
        }
        function attempt() {
            timer = undefined;
            if (stopped || !allowed())
                return;
            if (!patch) {
                const node = fibers(candidate => {
                    const type = candidate?.elementType;
                    return typeof type?.type === 'function' && source(type.type).includes('Settings.Root()');
                })[0];
                if (node) {
                    const owner = node.elementType;
                    const original = owner.type;
                    const wrapped = renderWrapper(original, after);
                    try {
                        owner.type = wrapped;
                        if (owner.type === wrapped)
                            patch = { owner, original, wrapped };
                    }
                    catch (error) {
                        warn('router patch', error);
                    }
                }
            }
            if (patch) {
                const { owner, original, wrapped } = patch;
                for (const node of fibers(candidate => candidate.elementType === owner)) {
                    replaceLiveType(node, original, wrapped);
                    if (!wrapperMounted && !tried.has(node)) {
                        const result = refreshFiber(node, host$1?.ReactDOM, React?.version);
                        host$1.diagnostics.routerRefresh = result;
                        if (result.scheduled) {
                            tried.add(node);
                            if (node.alternate)
                                tried.add(node.alternate);
                        }
                    }
                }
            }
            if (!wrapperMounted && attempts++ < 120)
                timer = setTimeout(attempt, 500);
        }
        function requestUpdate() {
            if (stopped)
                return;
            if (wrapperMounted) {
                bump();
                return;
            }
            if (timer === undefined)
                timer = setTimeout(attempt, 0);
        }
        return {
            get installed() { return !!patch; },
            addRoute(path, component, props) { routes.set(path, { component, props: props || {} }); requestUpdate(); },
            removeRoute(path) { routes.delete(path); requestUpdate(); },
            addPatch(path, value) { if (!patches.has(path))
                patches.set(path, new Set()); patches.get(path).add(value); requestUpdate(); return value; },
            removePatch(path, value) { patches.get(path)?.delete(value); requestUpdate(); return value; },
            addGlobalComponent(id, component) {
                const key = typeof id === 'string' ? id : 'global:' + globals.size;
                const value = (typeof id === 'string' ? component : id);
                if (value) {
                    globals.set(key, value);
                    requestUpdate();
                }
                return () => { globals.delete(key); requestUpdate(); };
            },
            removeGlobalComponent(id) { globals.delete(id); requestUpdate(); },
            uninstall() {
                if (stopped)
                    return;
                stopped = true;
                if (timer !== undefined)
                    clearTimeout(timer);
                routes.clear();
                patches.clear();
                globals.clear();
                for (const list of routeLists) {
                    try {
                        removeSlots(list);
                    }
                    catch { /* lista non piu' mutabile */ }
                }
                routeLists.clear();
                bump();
                if (patch) {
                    const { owner, original, wrapped } = patch;
                    if (owner.type === wrapped)
                        owner.type = original;
                    for (const node of fibers(candidate => candidate.elementType === owner)) {
                        replaceLiveType(node, wrapped, original);
                        refreshFiber(node, host$1?.ReactDOM, React?.version);
                    }
                }
                patch = null;
            },
        };
    }
    function installQamTab(options) {
        let stopped = false;
        let visible = true;
        let attached = false;
        let owner;
        let original;
        let wrappedView;
        let tabEnum;
        const enumOwned = [];
        let timer;
        const seen = new Set();
        const tries = new WeakMap();
        const wrappers = new WeakMap();
        const diagnostics = { renderCalls: 0, refresh: null, collision: false, mounted: [] };
        const tab = {
            key: PLAYHUB_TAB_KEY, strTitle: TAB_NAME,
            title: options.title ?? React.createElement(React.Fragment, null),
            tab: options.icon ?? React.createElement(React.Fragment, null),
            panel: options.content, vrLocation: 'quick-access-menu', __playhubTab: true,
        };
        function transform(tabs) {
            if (stopped || !allowed())
                return tabs;
            if (tabs.some(value => value?.key === PLAYHUB_TAB_KEY && value !== tab)) {
                diagnostics.collision = true;
                return tabs;
            }
            const next = tabs.includes(tab) ? tabs : tabs.slice();
            // La visibilita' si aggiorna fuori dal render. Non chiamiamo setter Steam
            // durante il render del padre, ne' tocchiamo schede che non sono nostre.
            if (!next.includes(tab))
                insertQamTab(next, tab, TAB_AFTER, visible);
            seen.add(next);
            if (seen.size > 64)
                seen.delete(seen.values().next().value);
            return next;
        }
        function wrap(inner) {
            const known = wrappers.get(inner);
            if (known)
                return known;
            const result = renderWrapper(inner, out => {
                if (stopped || !allowed())
                    return out;
                diagnostics.renderCalls++;
                try {
                    return mapRenderedTabs(out, transform, React);
                }
                catch (error) {
                    warn('qam tabs', error);
                    return out;
                }
            });
            wrappers.set(inner, result);
            return result;
        }
        function liveViews() {
            return owner ? fibers(node => node.elementType === owner || (typeof wrappedView === 'function' && node.type === wrappedView)) : [];
        }
        function snapshot() {
            const rows = [];
            for (const view of liveViews()) {
                // Si visita il sottoalbero del QAM, non i suoi fratelli: un altro menu
                // puo' avere una props tabs senza essere il nostro consumatore.
                const collect = (node) => {
                    const tabs = node.memoizedProps?.tabs;
                    if (Array.isArray(tabs))
                        rows.push({ tag: node.tag, keys: tabs.map((value) => value?.key), own: tabs.includes(tab) });
                };
                collect(view);
                if (view.child)
                    walkFibers(view.child, collect);
            }
            diagnostics.mounted = rows;
            return rows;
        }
        function registerEnum() {
            tabEnum = ui.QuickAccessTab ?? findTabEnum();
            if (!tabEnum)
                return false;
            if ((tabEnum[PLAYHUB_TAB_KEY] !== undefined && tabEnum[PLAYHUB_TAB_KEY] !== TAB_NAME) ||
                (tabEnum[TAB_NAME] !== undefined && tabEnum[TAB_NAME] !== PLAYHUB_TAB_KEY)) {
                diagnostics.collision = true;
                return false;
            }
            const missingNumber = tabEnum[PLAYHUB_TAB_KEY] === undefined;
            const missingName = tabEnum[TAB_NAME] === undefined;
            if (!registerTabKey(tabEnum, PLAYHUB_TAB_KEY, TAB_NAME))
                return false;
            if (missingNumber)
                enumOwned.push({ key: String(PLAYHUB_TAB_KEY), value: TAB_NAME });
            if (missingName && tabEnum[TAB_NAME] === PLAYHUB_TAB_KEY)
                enumOwned.push({ key: TAB_NAME, value: PLAYHUB_TAB_KEY });
            return true;
        }
        function tick() {
            timer = undefined;
            if (stopped)
                return;
            if (!allowed()) {
                uninstall();
                return;
            }
            try {
                if (!attached) {
                    owner = findQuickAccessView();
                    if (owner && typeof owner.type === 'function' && registerEnum()) {
                        original = owner.type;
                        wrappedView = wrap(original);
                        owner.type = wrappedView;
                        attached = owner.type === wrappedView;
                    }
                }
                if (attached && original && wrappedView) {
                    for (const node of liveViews()) {
                        replaceLiveType(node, original, wrappedView);
                        let hasTab = false;
                        const check = (candidate) => { if (candidate.memoizedProps?.tabs?.includes?.(tab))
                            hasTab = true; };
                        check(node);
                        if (node.child)
                            walkFibers(node.child, check);
                        const count = tries.get(node) || 0;
                        if (!hasTab && count < 3 && !diagnostics.collision) {
                            diagnostics.refresh = refreshFiber(node, host$1?.ReactDOM, React?.version);
                            if (diagnostics.refresh.scheduled) {
                                tries.set(node, count + 1);
                                if (node.alternate)
                                    tries.set(node.alternate, count + 1);
                            }
                        }
                    }
                }
                snapshot();
            }
            catch (error) {
                diagnostics.error = String(error);
                warn('qam attach', error);
            }
            if (!stopped)
                timer = setTimeout(tick, 1000);
        }
        function uninstall() {
            if (stopped)
                return;
            stopped = true;
            if (timer !== undefined)
                clearTimeout(timer);
            const views = liveViews();
            const collect = (node) => { const tabs = node.memoizedProps?.tabs; if (Array.isArray(tabs) && tabs.includes(tab))
                seen.add(tabs); };
            for (const view of views) {
                collect(view);
                if (view.child)
                    walkFibers(view.child, collect);
            }
            for (const tabs of seen) {
                try {
                    removeOwnedTab(tabs, tab);
                }
                catch { /* lista congelata: il render nativo la sostituira' */ }
            }
            if (owner && wrappedView && owner.type === wrappedView)
                owner.type = original;
            if (original && wrappedView)
                for (const node of views) {
                    replaceLiveType(node, wrappedView, original);
                    refreshFiber(node, host$1?.ReactDOM, React?.version);
                }
            // Durante un passaggio a Decky il valore primitivo puo' essere gia' suo:
            // l'uguaglianza della stringa non dimostra piu' la proprieta' della voce.
            const handoff = !allowed() || diagnostics.collision;
            for (const { key, value } of enumOwned) {
                try {
                    if (!handoff && tabEnum?.[key] === value)
                        delete tabEnum[key];
                }
                catch { /* enum non piu' modificabile */ }
            }
            enumOwned.length = 0;
            seen.clear();
            attached = false;
        }
        // Si parte fuori dal render che installa il plugin: flushSync non va mai
        // richiamato da un componente o da un effetto in corso di commit.
        timer = setTimeout(tick, 0);
        return {
            get inserted() { return !stopped && snapshot().some(row => row.own); },
            get attached() { return attached; },
            get diagnostics() { snapshot(); return { ...diagnostics }; },
            wrapTabsComponent: wrap,
            setVisible(next) {
                visible = next;
                tab.initialVisibility = next;
                if (!stopped && allowed() && typeof tab.qAMVisibilitySetter === 'function')
                    tab.qAMVisibilitySetter(next);
            },
            uninstall,
        };
    }
    function findQuickAccessView() {
        const carries = (value) => !!value && typeof value === 'object' && !!value.$$typeof &&
            typeof value.type === 'function' && source(value.type).includes('QuickAccessMenuBrowserView');
        try {
            // Il bootstrap conserva gli export di req(id): nessuna dipendenza da req.c.
            for (const value of host$1?.moduleExports || [])
                if (carries(value))
                    return value;
            if (typeof ui.findModuleExport === 'function') {
                const value = ui.findModuleExport(carries);
                if (value)
                    return value;
            }
            return fibers(node => carries(node.elementType))[0]?.elementType;
        }
        catch {
            return undefined;
        }
    }
    function findTabEnum() {
        const carries = (value) => value && typeof value === 'object' &&
            typeof value.Notifications === 'number' && typeof value.Settings === 'number' && value[value.Settings] === 'Settings';
        try {
            for (const value of host$1?.moduleExports || [])
                if (carries(value))
                    return value;
            return ui.findModuleByExport?.(carries);
        }
        catch {
            return undefined;
        }
    }
    function createToaster() {
        return {
            toast(options) {
                const title = String(options?.title ?? 'Playhub');
                const body = String(options?.body ?? '');
                try {
                    const notifications = window.SteamClient?.Notifications;
                    if (notifications?.DisplayNotification) {
                        notifications.DisplayNotification(title, body);
                        return;
                    }
                }
                catch { /* si mantiene il ripiego sul log */ }
                console.info('[Playhub]', title, body);
            },
        };
    }

    // Loads the plugin's OWN build (dist/index.js, the same file Decky loads) so the
    // standalone host shows the real Playhub interface instead of a substitute.
    //
    // The build is an ES module that expects a loader: Steam-side globals
    // (SP_REACT, SP_JSX, SP_REACTDOM, DFL), Decky's private loader-API global, and
    // its assets served from the loader's HTTP port. Rather than publishing those
    // globals — which would collide with a running Decky — the source is prepared
    // once and evaluated with the values passed in as locals.

    const DECKY_LOADER_GLOBAL =
      '__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit';
    const LOADER_PARAM = '__playhubStandaloneLoaderApi';
    const DEFAULT_EXPORT = /export\s*\{\s*([A-Za-z0-9_$]+)\s+as\s+default\s*\}\s*;?/;
    const ASSET_URL = /https?:\/\/127\.0\.0\.1:\d+\/plugins\/([A-Za-z0-9 ._-]+)\//g;
    const MODULE_KEYWORD = /^[ \t]*(?:import|export)\b/m;

    /**
     * Rewrite the built bundle into a function body.
     * @param {string} source the bundle text
     * @param {{assetBase: string}} options assetBase replaces the loader's asset URL, e.g. 'http://127.0.0.1:47993/plugin-assets/'
     */
    function prepareBundle(source, { assetBase }) {
      if (typeof source !== 'string' || source.length < 1024) throw new Error('The Playhub bundle is empty or truncated.');
      if (!assetBase || !/^https?:\/\/(127\.0\.0\.1|localhost)[:/]/.test(assetBase))
        throw new Error('The asset base must be a loopback URL.');
      const base = assetBase.endsWith('/') ? assetBase : assetBase + '/';
      let assets = 0;
      let code = source
        .replace(/^﻿/, '')
        .replace(/\/\/#\s*sourceMappingURL=.*$/gm, '')
        .replace(ASSET_URL, (_match, plugin) => { assets++; return base + plugin + '/'; });
      const exported = code.match(DEFAULT_EXPORT);
      if (!exported) throw new Error('The Playhub bundle has no default export to load.');
      code = code.replace(DEFAULT_EXPORT, `return ${exported[1]};`);
      const globalUses = code.split('window.' + DECKY_LOADER_GLOBAL).length - 1;
      code = code.split('window.' + DECKY_LOADER_GLOBAL).join(LOADER_PARAM);
      if (MODULE_KEYWORD.test(code)) throw new Error('The Playhub bundle still declares module imports or exports.');
      if (code.includes(DECKY_LOADER_GLOBAL)) throw new Error('The Playhub bundle still reads the Decky loader global.');
      return { code, assets, loaderReferences: globalUses, exportName: exported[1] };
    }

    /** The loader surface the build asks for: exactly what the plugin uses, nothing else. */
    function createLoaderApi({ call, routerHook, toaster, version = 2 }) {
      if (typeof call !== 'function') throw new Error('The standalone loader needs a backend call.');
      return {
        connect(requested, name) {
          if (typeof name !== 'string' || !name) throw new Error('A plugin name is required.');
          return { _version: Math.min(version, Number(requested) || version), call, routerHook, toaster };
        },
      };
    }

    /**
     * Evaluate the prepared bundle and return the plugin definition
     * ({name, titleView, content, icon, onDismount}).
     */
    function loadPluginBundle(source, { assetBase, globals, loaderApi, evaluate }) {
      const prepared = prepareBundle(source, { assetBase });
      const names = ['SP_REACT', 'SP_JSX', 'SP_REACTDOM', 'DFL'];
      const missing = names.filter((name) => !globals?.[name]);
      if (missing.length) throw new Error('Steam globals unavailable: ' + missing.join(', '));
      const factory = (defaultEvaluate)(prepared.code, [...names, LOADER_PARAM]);
      const definePluginResult = factory(...names.map((name) => globals[name]), loaderApi);
      if (typeof definePluginResult !== 'function') throw new Error('The Playhub bundle did not return a plugin factory.');
      const plugin = definePluginResult();
      if (!plugin || typeof plugin !== 'object') throw new Error('The Playhub plugin did not start.');
      return { plugin, prepared };
    }

    function defaultEvaluate(code, parameters) {
      // eslint-disable-next-line no-new-func
      return new Function(...parameters, '"use strict";\n' + code);
    }

    // Cooperative ownership for Playhub within one renderer/globalThis. This module
    // neither starts a host nor imports Steam, Decky or any application entry point.
    const REGISTRY = Symbol.for('playhub.renderer.ownership.v1');
    const PROTOCOL = 'playhub-renderer-ownership/1';
    const OWNERS = new Set(['decky', 'standalone']);

    class RendererOwnershipError extends Error {
      constructor(code, message, ownership = null) {
        super(message);
        this.name = 'RendererOwnershipError';
        this.code = code;
        this.ownership = ownership;
      }
    }

    function snapshot(record) {
      return record ? Object.freeze({
        owner: record.owner,
        generation: record.generation,
        state: record.state,
        failure: record.failure,
      }) : null;
    }

    function registryFor(renderer, create) {
      if ((typeof renderer !== 'object' && typeof renderer !== 'function') || renderer === null) {
        throw new TypeError('renderer must be an object representing one renderer global.');
      }
      let registry = Object.hasOwn(renderer, REGISTRY) ? renderer[REGISTRY] : undefined;
      if (registry === undefined && create) {
        registry = Object.seal({ protocol: PROTOCOL, generation: 0, active: null });
        Object.defineProperty(renderer, REGISTRY, { value: registry });
      }
      if (registry !== undefined && (registry?.protocol !== PROTOCOL || !Number.isSafeInteger(registry.generation))) {
        throw new RendererOwnershipError('INCOMPATIBLE_REGISTRY', 'The Playhub renderer registry has an incompatible protocol.');
      }
      return registry;
    }

    function abortError(reason) {
      const error = new Error('Playhub renderer mounting was cancelled.', { cause: reason });
      error.name = 'AbortError';
      return error;
    }

    /**
     * Atomically claims one renderer for Playhub. Duplicate attempts throw before
     * their mount callback runs. The returned lease is available during mounting.
     *
     * mount({signal, owner, generation, defer}) may return an async cleanup function.
     * Register partial cleanup with defer before initialization that can fail.
     * All initialization must be awaited by mount; detached work is not tracked.
     */
    function mountPlayhubRenderer({ owner, mount, signal, renderer = globalThis } = {}) {
      if (!OWNERS.has(owner)) throw new TypeError('owner must be decky or standalone.');
      if (typeof mount !== 'function') throw new TypeError('mount must be a function.');
      if (signal !== undefined && (typeof signal?.addEventListener !== 'function' || typeof signal?.removeEventListener !== 'function' || typeof signal?.aborted !== 'boolean')) {
        throw new TypeError('signal must be an AbortSignal.');
      }
      if (signal?.aborted) throw abortError(signal.reason);

      const registry = registryFor(renderer, true);
      if (registry.active) {
        throw new RendererOwnershipError('ALREADY_OWNED', 'Playhub already owns this renderer.', snapshot(registry.active));
      }
      if (registry.generation === Number.MAX_SAFE_INTEGER) {
        throw new RendererOwnershipError('GENERATION_EXHAUSTED', 'The Playhub renderer generation limit was reached.');
      }

      const controller = new AbortController();
      const record = { owner, generation: ++registry.generation, state: 'mounting', failure: null };
      registry.active = record;
      const cleanups = [];
      const registered = new Set();
      let mounting = true;
      let disposalRequested = false;
      let disposalPromise;
      let mountOutcome;
      let lease;

      function defer(cleanup) {
        if (typeof cleanup !== 'function') throw new TypeError('cleanup must be a function.');
        if (!mounting || registry.active !== record) {
          throw new RendererOwnershipError('LATE_CLEANUP_REGISTRATION', 'Register cleanup while mount is still pending.');
        }
        if (!registered.has(cleanup)) {
          registered.add(cleanup);
          cleanups.push(cleanup);
        }
      }

      function finalize() {
        if (disposalPromise) return disposalPromise;
        record.state = 'disposing';
        disposalPromise = (async () => {
          // A cancelled mount may still resolve with cleanup. Wait for that outcome
          // before taking the stack, retaining ownership for the entire interval.
          await mountOutcome;
          const failures = [];
          for (const cleanup of cleanups.reverse()) {
            try { await cleanup(); }
            catch (error) { failures.push(error); }
          }
          cleanups.length = 0;
          signal?.removeEventListener('abort', onAbort);
          if (failures.length) {
            record.state = 'failed';
            record.failure = 'cleanup-failed';
            // No force-unlock: a failed cleanup cannot establish safe handover.
            throw new AggregateError(failures, 'Playhub renderer cleanup failed; ownership remains blocked.');
          }
          record.state = 'disposed';
          if (registry.active === record) registry.active = null;
        })();
        // Callers still receive the rejection, but an AbortSignal listener need not
        // manufacture an unhandled rejection before the caller awaits the lease.
        disposalPromise.catch(() => {});
        return disposalPromise;
      }

      function dispose(reason) {
        if (!disposalRequested) {
          disposalRequested = true;
          controller.abort(reason);
        }
        return finalize();
      }

      function onAbort() { void dispose(signal.reason); }

      // Defer invocation until after the lease and abort listener are established.
      mountOutcome = Promise.resolve().then(async () => {
        if (disposalRequested) {
          mounting = false;
          return { ok: false, error: abortError(controller.signal.reason) };
        }
        try {
          const cleanup = await mount(Object.freeze({
            owner, generation: record.generation, signal: controller.signal, defer,
          }));
          if (cleanup !== undefined && cleanup !== null) defer(cleanup);
          return { ok: true };
        }
        catch (error) { return { ok: false, error }; }
        finally { mounting = false; }
      });

      const ready = mountOutcome.then(async ({ ok, error }) => {
        if (!ok || disposalRequested) {
          let cleanupError;
          try { await dispose(error ?? controller.signal.reason); }
          catch (failure) { cleanupError = failure; }
          if (cleanupError && !ok) {
            throw new AggregateError([error, cleanupError], 'Playhub mount and cleanup failed; ownership remains blocked.');
          }
          if (cleanupError) throw cleanupError;
          if (!ok) throw error;
          throw abortError(controller.signal.reason);
        }
        record.state = 'mounted';
        return lease;
      });
      ready.catch(() => {});
      lease = Object.freeze({
        owner, generation: record.generation, signal: controller.signal,
        ready, dispose, snapshot: () => snapshot(record),
      });
      signal?.addEventListener('abort', onAbort, { once: true });
      return lease;
    }

    // Il renderer carica la build originale del plugin, senza ricrearne l'interfaccia.
    // Il lease esterno resta l'arbitro; i controlli locali proteggono anche l'attesa asincrona.
    const host = window.__PLAYHUB_HOST__;
    const config = window.__PLAYHUB_HOST_CONFIG__ ?? {};
    const controller = new AbortController();
    const status = { state: 'mounting', diagnostics: host.diagnostics, dispose: () => lease.dispose() };
    window.__PLAYHUB_STANDALONE__ = status;
    const endpoint = String(config.baseUrl ?? '').replace(/\/$/, '');
    const bundleUrl = String(config.bundleUrl ?? endpoint + '/plugin-bundle');
    const assetBase = String(config.assetBase ?? endpoint + '/plugin-assets/');
    async function readBundle(signal) {
        const response = await fetch(bundleUrl, { signal, headers: { Authorization: 'Bearer ' + config.token } });
        if (!response.ok)
            throw new Error('The Playhub build could not be read: HTTP ' + response.status);
        return response.text();
    }
    const lease = mountPlayhubRenderer({
        owner: 'standalone',
        renderer: window,
        signal: controller.signal,
        async mount({ defer, signal }) {
            if (host.hasForeignRenderer?.())
                throw new Error('Another renderer owns Steam.');
            const source = await readBundle(signal);
            if (signal.aborted || host.hasForeignRenderer?.())
                throw new Error('Standalone mounting cancelled.');
            const routerHook = installRouterHook();
            defer(() => routerHook.uninstall());
            const toaster = createToaster();
            const loaderApi = createLoaderApi({
                call: (method, args) => host.call(method, ...(Array.isArray(args) ? args : [])),
                routerHook,
                toaster,
            });
            const { plugin, prepared } = loadPluginBundle(source, {
                assetBase,
                globals: { SP_REACT: host.React, SP_JSX: host.jsx, SP_REACTDOM: host.ReactDOM, DFL },
                loaderApi,
                evaluate: undefined,
            });
            defer(() => { try {
                plugin.onDismount?.();
            }
            catch (error) {
                console.warn('[Playhub standalone] onDismount', error);
            } });
            const qam = installQamTab({ title: plugin.titleView, icon: plugin.icon, content: plugin.content });
            defer(() => qam.uninstall());
            status.plugin = { name: plugin.name, assets: prepared.assets };
            status.surface = { qamAttached: () => qam.attached, qamInserted: () => qam.inserted, routerPatched: () => routerHook.installed, qamDiagnostics: () => qam.diagnostics };
            await host.event({ state: 'loaded', plugin: plugin.name, assets: prepared.assets });
        },
    });
    let disposal;
    const ownershipWatch = setInterval(() => {
        if (host.hasForeignRenderer?.())
            void status.dispose().catch((error) => console.warn('[Playhub standalone] dispose', error));
    }, 250);
    status.dispose = () => {
        if (disposal)
            return disposal;
        controller.abort();
        clearInterval(ownershipWatch);
        window.__PLAYHUB_BOOTSTRAP__?.cancel?.();
        disposal = (async () => {
            try {
                await lease.dispose();
            }
            finally {
                // La pulizia controlla l'identita': eventuali global sopraggiunti di Decky
                // non diventano nostri solo perche' hanno lo stesso nome.
                try {
                    host.releaseOwnedGlobals?.();
                }
                catch { /* gia' rimossi */ }
                status.state = 'disposed';
                await host.event({ state: 'disposed' });
            }
        })();
        return disposal;
    };
    status.ready = lease.ready
        .then(async () => {
        if (controller.signal.aborted)
            return;
        status.state = 'ready';
        await host.event({ state: 'ready', surface: 'original-plugin-build', diagnostics: host.diagnostics });
    })
        .catch(async (error) => {
        if (controller.signal.aborted)
            return;
        status.state = 'failed';
        status.error = String(error);
        await host.event({ state: 'failed', reason: String(error) });
    });

})();

})().catch(function(error){var h=window.__PLAYHUB_HOST__; if(h)h.event({state:"failed",reason:String(error&&error.message||error)}); console.error("[Playhub standalone] Mount failed",error);});

// Ponte verso le librerie che Decky mette a disposizione a tempo di
// esecuzione. Stessa forma usata da Launch Curtain: si importa da qui, non
// direttamente dai pacchetti, cosi' il punto di aggancio e' uno solo.
import { routerHook, definePlugin, toaster } from "@decky/api";
import * as DFL from "@decky/ui";
import * as SP_REACT from "react";
import * as SP_JSX from "react/jsx-runtime";

export { SP_REACT, SP_JSX, DFL, routerHook, definePlugin, toaster };

// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {SP_REACT as React} from "./decky";
import {type StoreDigitalGame} from "./StoreDigitalModels";
import {digitalStoreName} from "./StoreDigitalLocale";
import epic from "../assets/store-epic.png";
import gog from "../assets/store-gog.png";
import xbox from "../assets/store-xbox.png";
import amazon from "../assets/store-amazon.png";
import ea from "../assets/store-ea.png";
import ubisoft from "../assets/store-ubisoft.png";
import battlenet from "../assets/store-battlenet.png";

const storeLogos={epic,gog,xbox,amazon};
const partnerIdentities={ea:{name:"EA app",logo:ea},ubisoft:{name:"Ubisoft Connect",logo:ubisoft},battlenet:{name:"Battle.net",logo:battlenet}};
export function digitalGameStoreIdentity(game:Pick<StoreDigitalGame,"key"|"installProvider">){
 const provider=game.key.store==="xbox"?game.installProvider:null;
 if(provider&&Object.prototype.hasOwnProperty.call(partnerIdentities,provider))return{provider,...partnerIdentities[provider as keyof typeof partnerIdentities]};
 return{provider:game.key.store,name:digitalStoreName(game.key.store),logo:storeLogos[game.key.store]};
}
export function StoreDigitalGameStoreBadge({game}:{game:Pick<StoreDigitalGame,"key"|"installProvider">}){
 const identity=digitalGameStoreIdentity(game);
 return <div className="ph-sd-game-meta" data-install-provider={identity.provider}><img src={identity.logo} alt=""/>{identity.name}</div>;
}

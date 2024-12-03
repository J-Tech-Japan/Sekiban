"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.doSomeStuff = doSomeStuff;
console.log("Try npm run lint/fix!");
const longString = 'Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer ut aliquet diam.';
const trailing = 'Semicolon';
const why = { am: 'I tabbed?' };
const iWish = "I didn't have a trailing space...";
const sicilian = true;
;
const vizzini = (!!sicilian) ? !!!sicilian : sicilian;
const re = /foo   bar/;
function doSomeStuff(withThis, andThat, andThose) {
    //function on one line
    if (!Boolean(andThose.length)) {
        return false;
    }
    console.log(withThis);
    console.log(andThat);
    console.dir(andThose);
    console.log(longString, trailing, why, iWish, vizzini, re);
    return;
}
// TODO: more examples
//# sourceMappingURL=index.js.map
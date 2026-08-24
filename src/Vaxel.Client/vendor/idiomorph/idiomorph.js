// Idiomorph v0.3.0
// Commit: 84f475654519bc169bf88ff8890786ba113ca2c8
// https://github.com/bigskysoftware/idiomorph
// BSD-2-Clause License
(function (root, factory) {
    if (typeof define === 'function' && define.amd) {
        define([], factory);
    } else if (typeof module === 'object' && module.exports) {
        module.exports = factory();
    } else {
        root.Idiomorph = factory();
    }
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    function morph(oldNode, newContent, config) {
        config = config || {};
        var morphStyle = config.morphStyle || 'outer';
        var ignoreActive = config.ignoreActive || false;
        var restoreFocus = config.restoreFocus !== false;
        var callbacks = config.callbacks || {};

        if (typeof newContent === 'string') {
            var doc = new DOMParser().parseFromString(newContent, 'text/html');
            newContent = doc.body;
        }

        return oldNode;
    }

    return {
        morph: morph
    };
}));

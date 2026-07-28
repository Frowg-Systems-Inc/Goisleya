import js from "@eslint/js";
import globals from "globals";

// Lints the browser overlay scripts that ship inside the Isley package.
// These files run as classic scripts inside WebView2 pages, so they are
// linted as non-module browser code.
export default [
  {
    files: [
      "BurntHud/Map/isley-map-controller.js",
      "BurntHud/Voice/voice.js",
      "BurntHud/Voice/voice-crypto.js"
    ],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: "script",
      globals: {
        ...globals.browser
      }
    },
    rules: {
      ...js.configs.recommended.rules,
      // Overlay scripts intentionally swallow expected runtime failures
      // (WebView teardown, permission refusals) with empty catch blocks, and
      // sanitizer regexes intentionally match control characters.
      "no-empty": ["error", { allowEmptyCatch: true }],
      "no-control-regex": "off",
      "no-unused-vars": [
        "error",
        {
          args: "none",
          caughtErrors: "none",
          varsIgnorePattern: "^_"
        }
      ]
    }
  },
  {
    // voice-crypto.js also exports itself for the node contract suite.
    files: ["BurntHud/Voice/voice-crypto.js"],
    languageOptions: {
      globals: {
        module: "readonly"
      }
    }
  }
];

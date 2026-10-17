# My System hardware assembly

`hardware.js` is the complete original procedural model source. Its explicit box,
cylinder, ring, tube, and fan-blade geometry is bundled into the local renderer;
there is no remotely downloaded model or stock image. Materials are defined in
`../materials/palette.json`. One scene unit represents approximately 50 mm.

The assembly illustrates a generic ATX board, socketed CPU, a tower cooler with
copper heat pipes, a triple-fan graphics card, two DIMMs in a four-slot bank, M.2
storage, a SATA drive with cable, and two chassis fans on a test-bench rail.
It is **not** a scan or a physical inventory reconstruction of the user's PC.
Detected hardware details remain in the native application's monitoring panels.

All fans are intentionally stationary. Available fan RPM remains a numerical
native sensor reading. This scene does not map an ambiguous hardware sensor to
a specific illustrated rotor, and does not generate illustrative RPM.

The CPU exploded view lifts the cooler and heat spreader for inspection; it is
an engineering illustration, not an assembly or servicing instruction.

Original model/material source is covered by the repository's license. Three.js
is MIT licensed; its license is copied into the bundled renderer distribution.

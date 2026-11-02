# QA captures: farming round five (ADR-111)

Branch `claude/farming-round5` (from master `931a5d5`). Script `tests/browser/farm-round5-capture.cjs` (Development server, client published with
`-p:NosumDevTools=true`, `?qa=1`, saved language vi or en). Each file is one frame at the moment the message line shows the expected text, or after the
step's wait: `<lang>-NN-<step>-<W>x<H>.png`.

- **after** = this branch (`after/`): 1280x720, 1920x1080 and 390x844, vi and en.
- The 390x844 set is the width-override copy (`Device.MinimumWidth` 320 in that copy's `appsettings.json` only; the product keeps 1024).
- No **before** set. The old line was `Farm_Status_HasCrop` ("A crop already grows here."); it is still the reply for a pickaxe on a growing crop and for a
  seed on a mature crop that a wide swing's trailing tile reaches (ADR-111 D2). Those cases were not captured this round.

| Step | Expected line (vi / en) | Result |
|:--|:--|:--|
| `sown` (till, then seed tap) | "Đã gieo hạt." / "Seeds planted." | pass, all sizes |
| `seed-on-young-crop` (seed tap, day 0, unwatered) | "Chưa chín, còn 4 ngày" / "Not ready, 4 days left" | pass, all sizes |

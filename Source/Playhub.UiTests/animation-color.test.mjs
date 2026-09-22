import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const read = file => fs.readFileSync(new URL(`../Playhub/${file}`, import.meta.url), "utf8");
const source = read("MainWindow.AnimationSettings.cs");

test("custom drawer exposes only the existing three native H/S/B sliders", () => {
  assert.equal((source.match(/new Slider \{/g) || []).length, 3);
  assert.equal((source.match(/controls.Children.Add\(AnimationSlider\(/g) || []).length, 3);
  assert.doesNotMatch(source, /AnimationOpacity|opacityTrack|\"Opacita\"|PointerPressed|Tapped\s*\+=|IsMoveToPoint/);
  assert.match(source, /foreach \(var slider in new\[\] \{ hue, saturation, blackness \}\)\s+slider.ValueChanged/);
  assert.match(source, /if \(!refreshing && !_loadingGaming\) SelectCustom\(\)/);
  assert.match(source, /settings.AnimationUseCustomColor = true/);
  assert.match(source, /QueueSave\(\);/);
});

test("custom color fills the button rather than an inset content rectangle", () => {
  const button = source.split("var customButton = new Button")[1].split("};")[0];
  assert.doesNotMatch(button, /Content\s*=|\bHeight\s*=/);
  assert.match(button, /Padding = new Thickness\(0\)/);
  assert.match(button, /VerticalAlignment = VerticalAlignment.Stretch/);
  assert.match(button, /HorizontalAlignment = HorizontalAlignment.Stretch/);
  assert.doesNotMatch(source, /customFill/);
  assert.match(source, /customButton.Background = customBrush/);
  assert.match(source, /customButton.Resources\["ButtonBackground" \+ state\] = customBrush/);
});

test("preview gradients remain live and historic opacity stays in the persisted model", () => {
  for (const track of ["hueTrack", "saturationTrack", "blacknessTrack"]) {
    assert.ok(source.includes(`${track}.GradientStops.Clear()`));
    assert.ok(source.includes(`${track}.GradientStops.Add(new GradientStop { Offset = t, Color = AnimationCustomColor(`));
  }
  assert.match(read("Models/GamingModeConfig.cs"), /public double AnimationOpacity \{ get; set; \} = 100/);
  assert.match(source, /Color.FromArgb\(255,/);
});

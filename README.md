RuntimeIcons
============
[![GitHub Release](https://img.shields.io/github/v/release/LethalCompanyModding/RuntimeIcons?display_name=release&logo=github&logoColor=white)](https://github.com/LethalCompanyModding/RuntimeIcons/releases/latest)
[![GitHub Pre-Release](https://img.shields.io/github/v/release/LethalCompanyModding/RuntimeIcons?include_prereleases&display_name=release&logo=github&logoColor=white&label=pre-release)](https://github.com/LethalCompanyModding/RuntimeIcons/releases)  
[![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/LethalCompanyModding/RuntimeIcons?style=flat&logo=thunderstore&logoColor=white&label=thunderstore)](https://thunderstore.io/c/lethal-company/p/LethalCompanyModding/RuntimeIcons/)
#### ⚠️WARNING⚠️ this mod is currently in beta, future release might heavily change configs and/or rendered images

### Never lose track of your scrap again!

<p align="center">
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/Bell.png" alt="Bell Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/apparatus.png" alt="Apparatus Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/egg.png" alt="Easter Egg Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/tea_kettle.png" alt="Tea kettle Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/axle.png" alt="Large Axle Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/knife.png" alt="Knife Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/jug.png" alt="Chemical Jug Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/ammo.png" alt="Ammo Render" width="10%"/>
</p>

Runtime Icons brings a much-needed update to your hotbar! Enjoy breathtakingly-rendered scrap! With patent-pending technology your suit will scan each item you pick up and place its image on your hotbar so you know what to drop when that friendly neighborhood thumper rolls around the corner!

9/10 employees agree that with Runtime Icons, your productivity goes up up up, and you leave less blood on company scrap, which makes the Company happy!


## How it works

The first time an item that lacks an icon is spawned, it generates an icon to replace the gear icon in the HUD. These items are placed into the correct orientation (which can be overridden in the config) to properly display an image in the player's hotbar. This means any modded scrap should be compatible! Additional options are provided in the config.

**Note:** Modded scrap may not render if it has not been built correctly. Modders should refer to the [Mod Developer Information](#mod-developer-information) section.

<p align="center">
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/modded/scarlet_devil_mansion/painting.png" alt="SDM Painting Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/modded/testaccount_core/Crowbar.png" alt="Testaccount Crowbar Render" width="10%"/>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/modded/bell_crab.png" alt="Bell Crab Render" width="10%"/>
</p>

<details>
<summary>Gameplay Images</summary>

<p align="center">
<br>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/0a1afb9f5716f0f1736de9e767dc12aa3e291f70/.github/images/gameplay/gameplay2.png" alt="Gameplay Screenshot 3" width="85%"/>
<br>
<br>
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/0a1afb9f5716f0f1736de9e767dc12aa3e291f70/.github/images/gameplay/gameplay1.png" alt="Gameplay Screenshot 1" width="85%"/>
</p>

</details>


## Config

The following options are provided:

- **Blacklist/whitelist items** - Select whether to blacklist or whitelist items, then create a list of items to use.
- **Manual icon overrides** - Provide a mapping from item name to file path to choose an icon on the disk to use as its icon, in the form `Item A: Path/To/Icon A.png, Item B: Path/To/Icon B.png`
- **Manual rotation per item** - Customize the rotation of individual items in their rendered icons, by specifying their rotation in the form `Item A:x,y,z|Item B:x,y,z`, where `|` is the item separator. A tool is accessible via LethalConfig to combine rotations into a final `x,y,z` value to use.
- **Adjust icon emptiness threshold** - Specify the amount of fully transparent pixels allowed before the icon is considered empty.
- **Dump sprites to disk** - This option can be used to collect all generated icons in the `BepInEx/cache` folder. They will be stored in both PNG and EXR, where EXR fully retains specular highlights on transparent objects.


## Mod Developer Information

<p align="center">
<img src="https://raw.github.com/LethalCompanyModding/RuntimeIcons/1d1d58b2c29af5e8bf5b48a49fad87db79cde9c3/.github/images/renders/base_gear.png" alt="Default Gear Icon" width="10%"/>
</p>

### My custom item is invisible!

If an item you have created in the Unity editor without assigning an icon does not automatically receive an icon through Runtime Icons, please ensure that the HDRP's `Lit Shader Mode` is set to `Both`. This can be found under Edit → Project Settings → Quality → HDRP → Lit Shader Mode. This should be the default mode when using the Lethal Company Project Patcher.

**Technical explanation:** Due to a limitation in Lethal Company's HDRP settings, rendering a camera directly to a render texture with a transparent background results in an opaque image. To work around this, we have implemented a custom pass to render to a transparent render texture. However, custom passes are only able to render using the `Forward` mode, meaning that any objects that are built without those shaders will be invisible in the custom pass's output.

### My custom item is displayed at a weird angle!

You may want to adjust your resting rotation to get a better result. Items will normally be rendered from an angle similar to the perspective of a player that has dropped the item in front of them and then stood back and crouched. This angle will be adjusted somewhat based on the item's dimensions to give the image more depth.

**Note:** Only the X and Z values of the `restingRotation` vector of an item are used. To adjust the Y rotation, the `floorYOffset` should be used instead.

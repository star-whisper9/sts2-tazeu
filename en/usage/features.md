---
title: Features and Mechanics
---

# {{ $frontmatter.title }}

After connecting Coyote to experience the "current synergy", here are a few fun and thrilling core mechanics you can learn about.

## Combo Increment

![Combo Increment Diagram](/imgs/with_default_combo.png)

To create the ultimate "current synergy high", the Mod has a built-in **Combo Increment** system.

When you continuously trigger shocks in a short time (such as sequentially invoking multiple Lightning Orbs), the shock intensity will gradually stack and increment. This multiplier acts on the damage input end. With the compression characteristics of our specially tuned **Stevens's power law**, it brings a physical sensation of "getting more thrilling as it stacks, but without suddenly maxing out".

- **Increment Ratio Per Stack**: Each stack increases the equivalent damage by `15%` by default.
- **Combo Window**: The time window to maintain the combo, defaulting to `3 seconds`. If no combo is triggered within the timeout, the state resets.
- **Maximum Stack Layers**: Maximum stack defaults to `8 layers`.

## Scientific Current Mapping Algorithm

Human perception of electrical stimulation is not linear. To perfectly equate the "damage numbers" in the game with the "electrical shock stimulation" your body feels, we introduced the inverse mapping formula of **Stevens's power law** at the base level.

- The damage calculation will undergo a $1/3.5$ power calibration mapping.
- After amplitude limiting and compression, the specific physical intensity parameters issued are derived.
- This ultimately guarantees a natural transition in intensity, where low damage feels tingly and numb, while high damage provides a stimulating rhythm.

> [!NOTE] Tip
> The default algorithm tuning is on the radical side, suitable for thrill-seekers! If you find it a bit too much, you can adjust the "Damage Cap" in the configuration.

## One-to-Many Connection & Multiplayer Fix

Supports simultaneous connection of multiple DG-Lab Coyote 3.0 APPs! Scanning the same QR Code allows your friends to "suffer" along with you.

- **Independent Hardware**: Each APP is independently bound to its own Coyote hardware.
- **Event Broadcasting**: Shock events are broadcast to all bound clients! **(1 person plays, N people get shocked!)**
- **Client Management**: When the QR Code panel pops up, you can view all clients and manage them at any time with Kick or Block operations.

In multiplayer mode, we kept the optional feature where **all Defect players** can trigger the shock. By checking "**Only My Orbs**" in the configuration, your client will only respond to shocks triggered by your own Lightning Orbs, so you won't be accidentally shocked by your teammates' orbs during multiplayer!

## Quick Safe Test Feature

You no longer need to enter combat and find monsters to get hit just to test the intensity!

You can set the "**Test Damage**" value in the configuration, and then press the configured "**Test Shock**" hotkey to immediately trigger a shock with this damage value. This makes it convenient for you to repeatedly dial in the most comfortable strength and Waveform in a safe environment.

## Known Issues

The Block function in one-to-many connections is based on the client IP address (session-level, non-persistent). If used behind a reverse proxy/intranet penetration, all clients might share the same IP, meaning blocking one client could accidentally affect others, or the Block might completely fail. This limitation is determined by the DG-LAB WebSocket protocol and cannot be resolved on the Mod side.

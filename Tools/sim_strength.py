"""
TazeU 强度算法模拟测试
Stevens 幂律逆映射 + Combo 连击乘数

测试组:
  A: MinStrength=5,  ChannelLimit=40,  DamageCap=16
  B: MinStrength=20, ChannelLimit=100, DamageCap=16
  C: MinStrength=5,  ChannelLimit=40,  DamageCap=20

Combo 参数: rate=15%, max_stacks=8
"""

import math
import matplotlib.pyplot as plt
import matplotlib
import os

matplotlib.rcParams["font.sans-serif"] = ["Arial Unicode MS", "SimHei", "Heiti SC"]
matplotlib.rcParams["axes.unicode_minus"] = False


def map_damage_to_strength(damage: float, min_strength: int, max_strength: int, damage_cap: int) -> int:
    if max_strength <= min_strength:
        return max_strength
    ratio = math.pow(damage / damage_cap, 1.0 / 3.5)
    ratio = max(0.0, min(ratio, 1.0))
    return min_strength + round((max_strength - min_strength) * ratio)


def apply_combo(base_strength: int, channel_limit: int, multiplier: float) -> int:
    return min(round(base_strength * multiplier), channel_limit)


COMBO_RATE = 0.15
COMBO_MAX = 8

GROUPS = {
    "A (min=5, limit=40, cap=16)": {"min": 5, "limit": 40, "cap": 16},
    "B (min=20, limit=100, cap=16)": {"min": 20, "limit": 100, "cap": 16},
    "C (min=5, limit=40, cap=20)": {"min": 5, "limit": 40, "cap": 20},
}

# ── 测试 1: 无 combo，伤害从 1 递增到上限 ──

fig1, axes1 = plt.subplots(1, 3, figsize=(18, 5), sharey=False)
fig1.suptitle("测试 1: 伤害递增 → 强度（无 Combo）", fontsize=14)

for idx, (label, p) in enumerate(GROUPS.items()):
    damages = list(range(1, p["cap"] + 1))
    strengths = [map_damage_to_strength(d, p["min"], p["limit"], p["cap"]) for d in damages]

    ax = axes1[idx]
    ax.plot(damages, strengths, "o-", markersize=5, linewidth=2)
    ax.set_title(label)
    ax.set_xlabel("伤害值")
    ax.set_ylabel("输出强度")
    ax.set_xticks(damages)
    ax.grid(True, alpha=0.3)

fig1.tight_layout()
fig1.savefig(os.path.join(os.path.dirname(__file__), "test1_no_combo.png"), dpi=150)

# ── 测试 2: 启用 combo，伤害奇数步长，每个伤害值连续触发 10 次 ──

fig2, axes2 = plt.subplots(1, 3, figsize=(18, 6), sharey=False)
fig2.suptitle("测试 2: 伤害递增 × Combo 连击（每伤害值 10 次连续触发）", fontsize=14)

for idx, (label, p) in enumerate(GROUPS.items()):
    damages = list(range(1, p["cap"] + 1, 2))
    ax = axes2[idx]

    for dmg in damages:
        combo_strengths = []
        for combo in range(10):
            stack = min(combo, COMBO_MAX)
            mult = 1.0 + stack * COMBO_RATE
            effective_damage = dmg * mult
            s = map_damage_to_strength(effective_damage, p["min"], p["limit"], p["cap"])
            s = min(s, p["limit"])
            combo_strengths.append(s)
        ax.plot(range(10), combo_strengths, "o-", markersize=4, linewidth=1.5, label=f"dmg={dmg}")

    ax.set_title(label)
    ax.set_xlabel("连击次数 (combo)")
    ax.set_ylabel("输出强度")
    ax.legend(fontsize=7, loc="upper left")
    ax.grid(True, alpha=0.3)

fig2.tight_layout()
fig2.savefig(os.path.join(os.path.dirname(__file__), "test2_with_combo.png"), dpi=150)

print("图表已保存:")
print(f"  {os.path.join(os.path.dirname(__file__), 'test1_no_combo.png')}")
print(f"  {os.path.join(os.path.dirname(__file__), 'test2_with_combo.png')}")

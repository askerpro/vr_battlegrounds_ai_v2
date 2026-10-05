"""Чертёж исходного STL для проверки authored pivot; Assets не меняет."""
import json
import struct
from pathlib import Path

import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection

folder = Path("tmp/arsenal-holder-candidate/DDD")
data = (folder / "Quickhook-2in-Medium.stl").read_bytes()
count = struct.unpack_from("<I", data, 80)[0]
triangles = [
    [struct.unpack_from("<fff", data, 84 + i * 50 + 12 + j * 12) for j in range(3)]
    for i in range(count)
]
pivot = (600.4, 143.95, 4.55)
figure, axes = plt.subplots(1, 2, figsize=(11, 4))
for axis, coordinate, label in zip(axes, (1, 2), ("Y", "Z")):
    polygons = [[(v[0] - pivot[0], v[coordinate] - pivot[coordinate]) for v in tri] for tri in triangles]
    axis.add_collection(PolyCollection(polygons, facecolors="#838b91", edgecolors="#313840", linewidths=.1))
    axis.autoscale()
    axis.set_aspect("equal")
    axis.axvline(0, color="#db572d", linestyle="--", label="Proposed mounting plane")
    axis.plot(0, 0, "+", color="#db572d", markersize=12)
    axis.set_xlabel("X relative to proposed pivot (mm)")
    axis.set_ylabel(f"{label} relative to proposed pivot (mm)")
    axis.legend(fontsize=8)
figure.suptitle("DDD Quickhook 2in Medium — original geometry, proposed pivot")
figure.tight_layout()
figure.savefig(folder / "pivot-audit.png", dpi=140)
vertices = [v for triangle in triangles for v in triangle]
result = {
    "triangles": count,
    "pivotSourceMillimeters": pivot,
    "retainedShape": "rigid translation plus unit conversion, no deformation",
    "derivedBoundsMeters": {
        "min": [(min(v[a] for v in vertices) - pivot[a]) * .001 for a in range(3)],
        "max": [(max(v[a] for v in vertices) - pivot[a]) * .001 for a in range(3)],
    },
    "planeStatus": "proposal: two-inch tip distance is a unit check, not mounting-plane proof",
}
(folder / "pivot-proposal.json").write_text(json.dumps(result, indent=2), encoding="utf-8")

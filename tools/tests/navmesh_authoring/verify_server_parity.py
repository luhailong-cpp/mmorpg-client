"""用 Unity 实际导出参数烘焙同源 OBJ，再查询落盘 MSET，比对平面导航行为。"""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def invoke(command, cwd, log):
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True,
                            encoding="utf-8", timeout=60)
    log.write_text(result.stdout + result.stderr, encoding="utf-8")
    require(result.returncode == 0,
            f"Command failed ({result.returncode}): {command}; log: {log}")
    return result.stdout


def number(value):
    return format(float(value), ".9g")


def position(values):
    return ",".join(map(number, values))


def verify_case(case, baker, query, output):
    directory = Path(case["exportDirectory"])
    source = json.loads((directory / "source.json").read_text(encoding="utf-8"))
    for name, key in (("world-y-up.obj", "geometrySha256"),
                      ("navigation.json", "authoringSha256"),
                      ("server-bake-args.json", "serverBakeArgumentsSha256")):
        require(sha(directory / name) == source[key], f"Export SHA differs: {name}")
    arguments = json.loads((directory / "server-bake-args.json").read_text(encoding="utf-8"))
    require(len(arguments) % 2 == 0 and all(isinstance(arg, str) for arg in arguments),
            "Baker arguments must be explicit string pairs")
    options = dict(zip(arguments[::2], arguments[1::2]))
    agent = source["agent"]
    for key, field in (("--agent-radius", "radius"), ("--agent-height", "height"),
                       ("--agent-climb", "climb"), ("--agent-slope", "slope"),
                       ("--cs", "voxelSize")):
        require(abs(float(options[key]) - float(agent[field])) < 1e-6,
                f"Exported shared parameter differs: {key}")
    require(options["--profile"] == "authored-flat", "Wrong planar profile")
    require(options["--tile-size"] == "256" and float(options["--min-region-area"]) == 0,
            "Tile/region parameters differ from Unity contract")
    require(float(options["--probe"].split(",")[1]) == 0,
            "The original ground spawn height was changed")
    name = case["name"]
    bake_output = invoke([str(baker), *arguments], directory, output / f"{name}-bake.log")
    mesh = (directory / options["--out"]).resolve()
    require(mesh.is_file(), "Baker did not create an MSET")
    command = [str(query), "--nav", str(mesh)]
    for point in case["points"]:
        command += ["--point", position(point["input"])]
    for route in case["routes"]:
        command += ["--route", position(route["from"] + route["to"])]
    actual = json.loads(invoke(command, directory, output / f"{name}-query.json"))
    require(len(actual["points"]) == len(case["points"]), "Point result count differs")
    require(len(actual["routes"]) == len(case["routes"]), "Route result count differs")
    checks = []
    for expected, received in zip(case["points"], actual["points"]):
        require(bool(received["onMesh"]) == expected["onMesh"],
                f"{name}: point differs at {expected['input']}: Unity={expected['onMesh']} server={received}")
        if expected["onMesh"]:
            require(abs(received["nearest"][1]) <= .02 and abs(expected["nearest"][1]) <= .02,
                    f"{name}: original ground height differs")
            require(all(abs(a - b) <= .02 for a, b in zip(expected["nearest"], received["nearest"])),
                    f"{name}: paired nearest ground point differs")
        checks.append({"input": expected["input"], "onMesh": expected["onMesh"], "result": "PASS"})
    for expected, received in zip(case["routes"], actual["routes"]):
        require(bool(received["complete"]) == expected["complete"],
                f"{name}: complete-path result differs")
        require(received["raycast"] is not None and
                bool(received["raycast"]["hit"]) == expected["rayBlocked"],
                f"{name}: straight-line obstacle result differs")
        for corner in received["corners"]:
            require(abs(corner[1]) <= .02, f"{name}: server route left original ground")
        checks.append({"from": expected["from"], "to": expected["to"],
                       "complete": expected["complete"], "rayBlocked": expected["rayBlocked"], "result": "PASS"})
    return {"name": name, "result": "PASS", "objSha256": sha(directory / "world-y-up.obj"),
            "msetSha256": sha(mesh), "arguments": arguments, "checks": checks,
            "bakerOutput": bake_output, "serverQueries": actual}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-report", type=Path, required=True)
    parser.add_argument("--baker", type=Path, required=True)
    parser.add_argument("--query", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    report = {"result": "RUNNING", "scope": "Synthetic same-OBJ Unity/MSET queries; no production map or live-server acceptance",
              "unityQueriesSha256": sha(args.unity_report), "bakerSha256": sha(args.baker),
              "querySha256": sha(args.query), "cases": []}
    try:
        data = json.loads(args.unity_report.read_text(encoding="utf-8"))
        for case in data["cases"]:
            report["cases"].append(verify_case(case, args.baker.resolve(), args.query.resolve(), args.output.resolve()))
        report["result"] = "PASS"
    except Exception as error:
        report["result"] = "FAIL"
        report["error"] = str(error)
        raise
    finally:
        (args.output / "paired-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"PASS {len(report['cases'])} paired fixtures. Report: {args.output / 'paired-verification.json'}")


if __name__ == "__main__":
    main()

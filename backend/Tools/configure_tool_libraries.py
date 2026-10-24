"""Give packaged GDAL tools portable library lookup and ABI-name aliases."""
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
import os
import re
import subprocess
import sys


MACHO_MAGICS = {
    b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf",
    b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca",
}
DEPENDENCY_PATTERN = re.compile(
    r"^\s*(.*?) \(compatibility version ([^,]+), current version ([^)]+)\)\s*$"
)
VERSIONED_DYLIB_PATTERN = re.compile(r"^(?P<stem>.+?)\.(?P<version>\d+(?:\.\d+)*)\.dylib$")


@dataclass(frozen=True)
class Dependency:
    install_name: str
    compatibility_version: str
    current_version: str


@dataclass(frozen=True)
class DylibIdentity:
    install_name: str
    compatibility_version: str
    current_version: str


def fail(message):
    raise SystemExit(f"configure_tool_libraries.py: {message}")


def command_output(arguments):
    try:
        return subprocess.check_output(arguments, text=True, stderr=subprocess.STDOUT)
    except (OSError, subprocess.CalledProcessError) as error:
        output = getattr(error, "output", "") or str(error)
        fail(f"could not inspect {arguments[-1]}: {output.strip()}")


def is_macho(path):
    try:
        with path.open("rb") as stream:
            return stream.read(4) in MACHO_MAGICS
    except OSError as error:
        fail(f"could not read packaged image {path}: {error}")


def parse_dependencies(path):
    output = command_output(["/usr/bin/otool", "-L", str(path)])
    dependencies = {}
    for line in output.splitlines()[1:]:
        match = DEPENDENCY_PATTERN.match(line)
        if not match:
            continue
        item = Dependency(*(part.strip() for part in match.groups()))
        previous = dependencies.get(item.install_name)
        if previous is not None and previous != item:
            fail(f"{path} has architecture-dependent versions for {item.install_name}")
        dependencies[item.install_name] = item
    return tuple(dependencies.values())


def parse_dylib_identity(path):
    ids = {
        line.strip()
        for line in command_output(["/usr/bin/otool", "-D", str(path)]).splitlines()[1:]
        if line.strip() and not line.strip().endswith(":")
    }
    if len(ids) != 1:
        fail(f"{path} must declare one consistent LC_ID_DYLIB; found {sorted(ids)}")
    install_name = next(iter(ids))
    matching = [item for item in parse_dependencies(path) if item.install_name == install_name]
    if len(matching) != 1:
        fail(f"{path} LC_ID_DYLIB has no unique compatibility/current version")
    item = matching[0]
    return DylibIdentity(item.install_name, item.compatibility_version, item.current_version)


def dylib_family(install_name):
    leaf = PurePosixPath(install_name).name
    match = VERSIONED_DYLIB_PATTERN.fullmatch(leaf)
    if match:
        version = tuple(int(part) for part in match.group("version").split("."))
        return match.group("stem"), version
    if leaf.endswith(".dylib"):
        return leaf[:-len(".dylib")], ()
    return None


def compatible_identity(requested, identity):
    requested_family = dylib_family(requested.install_name)
    actual_family = dylib_family(identity.install_name)
    if requested_family is None or actual_family is None:
        return requested.install_name == identity.install_name
    requested_stem, requested_version = requested_family
    actual_stem, actual_version = actual_family
    same_versioned_name = requested_version == actual_version
    install_name_version_alias = (
        bool(requested_version)
        and len(actual_version) > len(requested_version)
        and actual_version[:len(requested_version)] == requested_version
    )
    return (
        requested_stem == actual_stem
        and (same_versioned_name or install_name_version_alias)
        and requested.compatibility_version == identity.compatibility_version
        and requested.current_version == identity.current_version
    )


def safe_rpath_target(library_directory, install_name):
    if not install_name.startswith("@rpath/"):
        return None
    relative = PurePosixPath(install_name[len("@rpath/"):])
    if relative.is_absolute() or not relative.parts or any(part in ("", ".", "..") for part in relative.parts):
        fail(f"unsafe packaged @rpath dependency {install_name}")
    return library_directory.joinpath(*relative.parts)


def inspect_packaged_dylibs(library_directory):
    candidates = {}
    for path in sorted(library_directory.glob("*.dylib")):
        if not path.is_file() or not is_macho(path):
            continue
        resolved = path.resolve(strict=True)
        try:
            resolved.relative_to(library_directory.resolve())
        except ValueError:
            fail(f"packaged dylib escapes the native directory: {path} -> {resolved}")
        candidates.setdefault(resolved, parse_dylib_identity(resolved))
    if not candidates:
        fail(f"no packaged Mach-O dylibs found in {library_directory}")
    return candidates


def dependency_target(requested_path, dependency, candidates, library_directory):
    if requested_path.is_symlink() and not requested_path.exists():
        fail(f"dangling packaged install-name alias: {requested_path}")
    if requested_path.exists():
        if not requested_path.is_file() or not is_macho(requested_path):
            fail(f"packaged dependency is not a Mach-O dylib: {requested_path}")
        target = requested_path.resolve(strict=True)
        try:
            target.relative_to(library_directory.resolve())
        except ValueError:
            fail(f"packaged dependency escapes the native directory: {requested_path} -> {target}")
        identity = candidates.get(target)
        if identity is None:
            identity = parse_dylib_identity(target)
            candidates[target] = identity
        if not compatible_identity(dependency, identity):
            fail(
                f"conflicting install-name identity for {dependency.install_name}: "
                f"{target} declares {identity.install_name} "
                f"(compatibility {identity.compatibility_version}, current {identity.current_version})"
            )
        return None

    matches = [
        path for path, identity in candidates.items()
        if compatible_identity(dependency, identity)
    ]
    if len(matches) != 1:
        if not matches:
            fail(
                f"missing packaged dependency {dependency.install_name} "
                f"(compatibility {dependency.compatibility_version}, current {dependency.current_version})"
            )
        fail(f"ambiguous packaged dependency {dependency.install_name}: {matches}")
    return matches[0]


def relative_install_name_aliases(library_directory, tool_dependencies, candidates):
    aliases = {}
    for tool, dependencies in tool_dependencies:
        for dependency in dependencies:
            target = safe_rpath_target(library_directory, dependency.install_name)
            if target is None:
                continue
            candidate = dependency_target(target, dependency, candidates, library_directory)
            if candidate is None:
                continue
            previous = aliases.get(target)
            if previous is not None and previous != candidate:
                fail(f"conflicting targets requested for install-name alias {target}")
            aliases[target] = candidate
    for alias, candidate in aliases.items():
        alias.parent.mkdir(parents=True, exist_ok=True)
        if alias.exists() or alias.is_symlink():
            fail(f"install-name alias appeared during configuration: {alias}")
        relative_target = os.path.relpath(candidate, alias.parent)
        os.symlink(relative_target, alias)
        resolved = alias.resolve(strict=True)
        if resolved != candidate:
            fail(f"created install-name alias does not resolve to its inspected dylib: {alias}")
    return len(aliases)


def configure_tool_rpath(path, required):
    load_commands = command_output(["/usr/bin/otool", "-l", str(path)])
    rpaths = re.findall(r"cmd LC_RPATH\s+cmdsize \d+\s+path (.+?) \(offset", load_commands)
    if required in rpaths:
        return False
    signature = subprocess.run(
        ["/usr/bin/codesign", "-d", str(path)],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.PIPE,
    )
    if signature.returncode == 0:
        subprocess.run(["/usr/bin/codesign", "--remove-signature", str(path)], check=True)
    subprocess.run(["/usr/bin/install_name_tool", "-add_rpath", required, str(path)], check=True)
    subprocess.run(
        ["/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", str(path)],
        check=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.PIPE,
    )
    subprocess.run(["/usr/bin/codesign", "--verify", "--strict", str(path)], check=True)
    return True


def main():
    directory = Path(sys.argv[1]).resolve()
    native = directory / "runtimes" / "osx-arm64" / "native"
    if native.is_dir():
        library_directory = native
        required = "@loader_path/runtimes/osx-arm64/native"
    elif any(directory.glob("libgdal*.dylib")):
        # RID-specific .NET publishing flattens runtime assets into PublishDir.
        library_directory = directory
        required = "@loader_path"
    else:
        return

    tools = []
    for path in sorted(directory.iterdir()):
        if not path.is_file() or not path.name.startswith(("gdal", "ogr", "gnm", "nearblack", "sozip")):
            continue
        if is_macho(path):
            tools.append((path, parse_dependencies(path)))
    if not tools:
        return

    candidates = inspect_packaged_dylibs(library_directory)
    alias_count = relative_install_name_aliases(library_directory, tools, candidates)
    configured_count = sum(configure_tool_rpath(path, required) for path, _ in tools)
    if alias_count or configured_count:
        print(
            f"Configured {configured_count} GDAL tool rpaths and "
            f"{alias_count} ABI-verified relative install-name aliases."
        )


if __name__ == "__main__":
    main()

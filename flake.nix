{
  description = "Runesmith, a code editor built on .NET and Avalonia";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { nixpkgs, ... }:
    let
      pkgs = nixpkgs.legacyPackages.x86_64-linux;
      # Avalonia and SkiaSharp load these by name at run time, so they have to be on the library path for apps started from the shell.
      runtimeLibraries = with pkgs; [
        expat
        fontconfig
        freetype
        libGL
        libice
        libsm
        libx11
        libxcursor
        libxext
        libxfixes
        libxi
        libxrandr
      ];
    in
    {
      devShells.x86_64-linux.default = pkgs.mkShell {
        packages = [
          pkgs.dotnet-sdk_10
          pkgs.nodejs_22
          pkgs.xvfb-run
        ];
        LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath runtimeLibraries;
        DOTNET_ROOT = "${pkgs.dotnet-sdk_10}/share/dotnet";
      };
    };
}

Fix for MC6029 RootNamespace error.

Unzip into repo root (overwrite). Then Build -> Rebuild.
If you still see MC6029, open your .csproj and ensure:
  <RootNamespace>DAISY_Braille_Toolkit</RootNamespace>
No hyphens/spaces allowed.

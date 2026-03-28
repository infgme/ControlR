global using Xunit;

#if IS_WINDOWS
global using ControlR.DesktopClient.Windows;
#elif IS_LINUX
global using ControlR.DesktopClient.Linux;
#elif IS_MACOS
global using ControlR.DesktopClient.Mac;
#endif
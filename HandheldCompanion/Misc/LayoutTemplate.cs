﻿using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Actions;
using HandheldCompanion.Controllers;
using HandheldCompanion.Inputs;
using Newtonsoft.Json;
using SharpDX.XInput;
using System;
using System.Collections.Generic;

namespace HandheldCompanion.Misc
{
    [JsonObject(MemberSerialization.OptIn)]
    public partial class LayoutTemplate : IComparable
    {
        public static readonly LayoutTemplate DesktopLayout =
            new("Desktop", "Layout for Desktop Browsing", "HandheldCompanion", true);

        public static readonly LayoutTemplate DefaultLayout = new("Gamepad (XBOX)",
            "This template is for games that already have built-in gamepad support. Intended for dual stick games such as twin-stick shooters, side-scrollers, etc.",
            "HandheldCompanion", true);

        public static readonly LayoutTemplate NintendoLayout = new("Gamepad (Nintendo)",
            "This template is for games that already have built-in gamepad support. Intended for games that are designed with a Nintendo gamepad in mind.",
            "HandheldCompanion", true);

        public static readonly LayoutTemplate KeyboardLayout = new("Keyboard (WASD) and Mouse",
            "This template works great for the games that were designed with a keyboard and mouse in mind, without gamepad support. The controller will drive the game's keyboard based events with buttons, but will make assumptions about which buttons move you around (WASD for movement, space for jump, etc.). The right pad will emulate the movement of a mouse.",
            "HandheldCompanion", true);

        public static readonly LayoutTemplate GamepadMouseLayout = new("Gamepad with Mouse Trackpad",
            "This template is for games that already have built-in gamepad support. The right trackpad will be bound to mouse emulation which may not work in all games.",
            "HandheldCompanion", true, typeof(NeptuneController));

        public static readonly LayoutTemplate GamepadJoystickLayout = new("Gamepad with Joystick Trackpad",
            "This template is for games that already have built-in gamepad support and have a third person controlled camera. FPS or Third Person Adventure games, etc.",
            "HandheldCompanion", true, typeof(NeptuneController));

        public LayoutTemplate()
        {
        }

        public LayoutTemplate(Layout layout) : this()
        {
            Layout = layout;
            Layout.Updated += Layout_Updated;
        }

        private LayoutTemplate(string name, string description, string author, bool isInternal, Type deviceType = null) : this()
        {
            Name = name;
            Description = description;
            Author = author;
            Product = string.Empty;

            IsInternal = isInternal;
            ControllerType = deviceType;

            Layout = new Layout();
            Layout.FillDefault();

            switch (Name)
            {
                default:
                case "Gamepad (XBOX)":
                    break;

                case "Desktop":
                    {
                        Layout.AxisLayout = new()
                        {
                            { AxisLayoutFlags.LeftStick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Scroll } } },
                            { AxisLayoutFlags.RightStick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Move } } },
                            { AxisLayoutFlags.LeftPad, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Scroll } } },
                            { AxisLayoutFlags.RightPad, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Move } } },
                            {
                                AxisLayoutFlags.L2, new List<IActions>()
                                {
                                    new MouseActions
                                    {
                                        motionThreshold = Gamepad.TriggerThreshold,
                                        motionDirection = Utils.MotionDirection.Up,
                                        MouseType = MouseActionsType.RightButton
                                    }
                                }
                            },
                            {
                                AxisLayoutFlags.R2, new List<IActions>()
                                {
                                    new MouseActions
                                    {
                                        motionThreshold = Gamepad.TriggerThreshold,
                                        motionDirection = Utils.MotionDirection.Up,
                                        MouseType = MouseActionsType.LeftButton
                                    }
                                }
                            }
                        };

                        Layout.ButtonLayout = new()
                        {
                            { ButtonFlags.B1, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.RETURN } } },
                            { ButtonFlags.B2, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.ESCAPE } } },
                            { ButtonFlags.B3, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.PRIOR } } },
                            { ButtonFlags.B4, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.NEXT } } },

                            { ButtonFlags.L1, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.BACK } } },
                            { ButtonFlags.R1, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.SPACE } } },

                            { ButtonFlags.Back, new List<IActions> { new KeyboardActions() { Key = VirtualKeyCode.TAB, Modifiers = ModifierSet.Alt } } },
                            { ButtonFlags.Start, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.TAB } } },

                            { ButtonFlags.DPadUp, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.UP } } },
                            { ButtonFlags.DPadDown, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.DOWN } } },
                            { ButtonFlags.DPadLeft, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.LEFT } } },
                            { ButtonFlags.DPadRight, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.RIGHT } } },

                            { ButtonFlags.LeftPadClick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.RightButton } } },
                            { ButtonFlags.RightPadClick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.LeftButton } } }
                        };
                    }
                    break;

                case "Gamepad (Nintendo)":
                    {
                        Layout.ButtonLayout[ButtonFlags.B1] = [new ButtonActions { Button = ButtonFlags.B2 }];
                        Layout.ButtonLayout[ButtonFlags.B2] = [new ButtonActions { Button = ButtonFlags.B1 }];
                        Layout.ButtonLayout[ButtonFlags.B3] = [new ButtonActions { Button = ButtonFlags.B4 }];
                        Layout.ButtonLayout[ButtonFlags.B4] = [new ButtonActions { Button = ButtonFlags.B3 }];
                    }
                    break;

                case "Keyboard (WASD) and Mouse":
                    {
                        Layout.AxisLayout = new()
                        {
                            { AxisLayoutFlags.RightStick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Move } } },
                            { AxisLayoutFlags.RightPad, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Move } } },
                            {
                                AxisLayoutFlags.LeftStick, new List<IActions>()
                                {
                                    new KeyboardActions
                                    {
                                        motionThreshold = Gamepad.LeftThumbDeadZone,
                                        motionDirection = Utils.MotionDirection.Left,
                                        Key = VirtualKeyCode.VK_A
                                    },
                                    new KeyboardActions
                                    {
                                        motionThreshold = Gamepad.LeftThumbDeadZone,
                                        motionDirection = Utils.MotionDirection.Right,
                                        Key = VirtualKeyCode.VK_D
                                    },
                                    new KeyboardActions
                                    {
                                        motionThreshold = Gamepad.LeftThumbDeadZone,
                                        motionDirection = Utils.MotionDirection.Up,
                                        Key = VirtualKeyCode.VK_W
                                    },
                                    new KeyboardActions
                                    {
                                        motionThreshold = Gamepad.LeftThumbDeadZone,
                                        motionDirection = Utils.MotionDirection.Down,
                                        Key = VirtualKeyCode.VK_S
                                    }
                                }
                            },
                            {
                                AxisLayoutFlags.L2, new List<IActions>()
                                {
                                    new MouseActions
                                    {
                                        motionThreshold = Gamepad.TriggerThreshold,
                                        motionDirection = Utils.MotionDirection.Up,
                                        MouseType = MouseActionsType.RightButton
                                    }
                                }
                            },
                            {
                                AxisLayoutFlags.R2, new List<IActions>()
                                {
                                    new MouseActions
                                    {
                                        motionThreshold = Gamepad.TriggerThreshold,
                                        motionDirection = Utils.MotionDirection.Up,
                                        MouseType = MouseActionsType.LeftButton
                                    }
                                }
                            }
                        };

                        Layout.ButtonLayout = new()
                        {
                            { ButtonFlags.B1, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.SPACE } } },
                            { ButtonFlags.B2, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_E } } },
                            { ButtonFlags.B3, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_R } } },
                            { ButtonFlags.B4, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_F } } },

                            { ButtonFlags.L1, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.ScrollDown } } },
                            { ButtonFlags.R1, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.ScrollUp } } },

                            { ButtonFlags.Back, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.TAB } } },
                            { ButtonFlags.Start, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.ESCAPE } } },

                            { ButtonFlags.DPadUp, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_1 } } },
                            { ButtonFlags.DPadDown, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_3 } } },
                            { ButtonFlags.DPadLeft, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_4 } } },
                            { ButtonFlags.DPadRight, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_2 } } },

                            { ButtonFlags.LeftStickClick, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.LSHIFT } } },
                            { ButtonFlags.RightStickClick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.LeftButton } } },

                            { ButtonFlags.LeftPadClickUp, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_1 } } },
                            { ButtonFlags.LeftPadClickDown, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_3 } } },
                            { ButtonFlags.LeftPadClickLeft, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_4 } } },
                            { ButtonFlags.LeftPadClickRight, new List<IActions>() { new KeyboardActions { Key = VirtualKeyCode.VK_2 } } },

                            { ButtonFlags.RightPadClick, new List<IActions>() { new MouseActions { MouseType = MouseActionsType.LeftButton } } }
                        };
                    }
                    break;

                case "Gamepad with Mouse Trackpad":
                    {
                        Layout.AxisLayout[AxisLayoutFlags.RightPad] = new List<IActions>() { new MouseActions { MouseType = MouseActionsType.Move } };
                    }
                    break;

                case "Gamepad with Joystick Trackpad":
                    {
                        Layout.AxisLayout[AxisLayoutFlags.RightPad] = new List<IActions>() { new AxisActions { Axis = AxisLayoutFlags.RightStick } };
                    }
                    break;
            }
        }

        [JsonProperty] public string Author { get; set; } = string.Empty;
        [JsonProperty] public string Name { get; set; } = string.Empty;
        [JsonProperty] public string Description { get; set; } = string.Empty;
        [JsonProperty] public string Product { get; set; } = string.Empty;
        [JsonProperty] public Guid Guid { get; set; } = Guid.NewGuid();
        [JsonProperty] public string Executable { get; set; } = string.Empty;
        [JsonProperty] public bool IsInternal { get; set; } = false;
        [JsonProperty] public Layout Layout { get; set; } = new();
        [JsonProperty] public Type ControllerType { get; set; }

        public int CompareTo(object obj)
        {
            var profile = (LayoutTemplate)obj;
            return profile.Name.CompareTo(Name);
        }

        public void ClearDelegates()
        {
            Updated = null;
        }

        private void Layout_Updated(Layout layout)
        {
            Updated?.Invoke(this);
        }

        #region events

        public event UpdatedEventHandler Updated;

        public delegate void UpdatedEventHandler(LayoutTemplate layoutTemplate);

        #endregion
    }
}

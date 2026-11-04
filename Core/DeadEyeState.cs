// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

namespace DeadEye.Core;

/// <summary>
/// States of the Dead Eye flow.
/// Inactive -> Marking (bullet time, marks accumulate) -> Exiting (time restored)
///   -> Firing (rapid shot sequence at normal speed) -> Inactive.
/// </summary>
internal enum DeadEyeState
{
    Inactive,
    Marking,
    Exiting,
    Firing,
}

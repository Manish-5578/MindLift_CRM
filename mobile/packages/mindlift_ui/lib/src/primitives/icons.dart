import 'package:flutter/material.dart' show Icons;
import 'package:flutter/widgets.dart';
import '../tokens/colors.dart';

/// One icon family for the whole app: Material Symbols (rounded), the set Android users already know.
/// Use [MlIcon] instead of emoji or text glyphs, so every icon shares weight, size and colour rules.
class MlIcons {
  const MlIcons._();

  // Navigation
  static const home = Icons.home_rounded;
  static const homeOutline = Icons.home_outlined;
  static const leads = Icons.business_center_rounded;
  static const leadsOutline = Icons.business_center_outlined;
  static const chat = Icons.chat_bubble_rounded;
  static const chatOutline = Icons.chat_bubble_outline_rounded;
  static const profile = Icons.person_rounded;
  static const profileOutline = Icons.person_outline_rounded;
  static const add = Icons.add_rounded;
  static const back = Icons.arrow_back_rounded;
  static const chevronLeft = Icons.chevron_left_rounded;
  static const chevronRight = Icons.chevron_right_rounded;
  static const close = Icons.close_rounded;
  static const more = Icons.more_horiz_rounded;

  // Actions
  static const search = Icons.search_rounded;
  static const bell = Icons.notifications_none_rounded;
  static const bellActive = Icons.notifications_rounded;
  static const edit = Icons.edit_outlined;
  static const delete = Icons.delete_outline_rounded;
  static const check = Icons.check_rounded;
  static const checkCircle = Icons.check_circle_rounded;
  static const copy = Icons.content_copy_rounded;
  static const reply = Icons.reply_rounded;
  static const send = Icons.send_rounded;
  static const attach = Icons.attach_file_rounded;
  static const camera = Icons.photo_camera_outlined;
  static const gallery = Icons.photo_library_outlined;
  static const document = Icons.description_outlined;
  static const mic = Icons.mic_rounded;
  static const play = Icons.play_arrow_rounded;
  static const pause = Icons.pause_rounded;
  static const scan = Icons.document_scanner_outlined;
  static const folder = Icons.folder_open_rounded;
  static const note = Icons.sticky_note_2_outlined;
  static const list = Icons.view_list_rounded;
  static const board = Icons.view_kanban_outlined;
  static const logout = Icons.logout_rounded;
  static const lock = Icons.lock_outline_rounded;
  static const fingerprint = Icons.fingerprint_rounded;
  static const down = Icons.keyboard_arrow_down_rounded;
  static const warning = Icons.warning_amber_rounded;
  static const sparkle = Icons.auto_awesome_rounded;
  static const handshake = Icons.handshake_outlined;
  static const person = Icons.person_outline_rounded;
  static const swap = Icons.swap_horiz_rounded;
  static const calendar = Icons.calendar_month_rounded;
  static const settings = Icons.tune_rounded;
  static const phone = Icons.smartphone_rounded;
  static const refresh = Icons.refresh_rounded;
  static const pending = Icons.schedule_rounded;
  static const sent = Icons.done_rounded;
  static const read = Icons.done_all_rounded;
  static const selectOn = Icons.check_circle_rounded;
  static const radioOn = Icons.radio_button_checked_rounded;
  static const selectOff = Icons.radio_button_unchecked_rounded;

  // Reach someone
  static const call = Icons.call_rounded;
  static const whatsapp = Icons.forum_rounded;
  static const sms = Icons.sms_rounded;
  static const email = Icons.mail_rounded;

  // Appearance presets
  static const themeAuto = Icons.brightness_auto_rounded;
  static const themeLight = Icons.light_mode_rounded;
  static const themeDark = Icons.dark_mode_rounded;
}

/// A sized, coloured icon. The colour follows the current theme unless given.
class MlIcon extends StatelessWidget {
  const MlIcon(this.icon, {super.key, this.size = 22, this.color});
  final IconData icon;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) =>
      Icon(icon, size: size, color: color ?? MlColors.textPrimary);
}

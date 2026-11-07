class RegisterMobileDeviceRequest {
  const RegisterMobileDeviceRequest({
    required this.installationId,
    required this.platform,
    this.environment = 'production',
    this.provider = 'fcm',
    required this.deviceToken,
    this.appVersion = '1.0.0',
    this.locale = 'en',
    this.timeZoneId = 'UTC',
    this.notificationsAllowed = true,
  });

  final String installationId;
  final String platform; // "android" or "ios"
  final String environment; // "sandbox" or "production"
  final String provider; // "fcm" or "apns"
  final String deviceToken;
  final String appVersion;
  final String locale;
  final String timeZoneId;
  final bool notificationsAllowed;

  Map<String, dynamic> toJson() => {
        'installationId': installationId,
        'platform': platform,
        'environment': environment,
        'provider': provider,
        'deviceToken': deviceToken,
        'appVersion': appVersion,
        'locale': locale,
        'timeZoneId': timeZoneId,
        'notificationsAllowed': notificationsAllowed,
      };
}

class MobileDeviceHeartbeatRequest {
  const MobileDeviceHeartbeatRequest({
    required this.installationId,
    required this.appVersion,
    this.locale = 'en',
    this.timeZoneId = 'UTC',
    this.notificationsAllowed = true,
  });

  final String installationId;
  final String appVersion;
  final String locale;
  final String timeZoneId;
  final bool notificationsAllowed;

  Map<String, dynamic> toJson() => {
        'installationId': installationId,
        'appVersion': appVersion,
        'locale': locale,
        'timeZoneId': timeZoneId,
        'notificationsAllowed': notificationsAllowed,
      };
}

class MobileDeviceDto {
  const MobileDeviceDto({
    required this.id,
    required this.installationId,
    required this.platform,
    required this.environment,
    required this.provider,
    required this.appVersion,
    required this.locale,
    required this.timeZoneId,
    required this.notificationsAllowed,
    required this.lastSeenAtUtc,
    this.revokedAtUtc,
  });

  final String id;
  final String installationId;
  final String platform;
  final String environment;
  final String provider;
  final String appVersion;
  final String locale;
  final String timeZoneId;
  final bool notificationsAllowed;
  final DateTime lastSeenAtUtc;
  final DateTime? revokedAtUtc;

  factory MobileDeviceDto.fromJson(Map<String, dynamic> json) {
    return MobileDeviceDto(
      id: json['id'] as String? ?? '',
      installationId: json['installationId'] as String? ?? '',
      platform: json['platform'] as String? ?? '',
      environment: json['environment'] as String? ?? 'production',
      provider: json['provider'] as String? ?? 'fcm',
      appVersion: json['appVersion'] as String? ?? '',
      locale: json['locale'] as String? ?? 'en',
      timeZoneId: json['timeZoneId'] as String? ?? 'UTC',
      notificationsAllowed: json['notificationsAllowed'] as bool? ?? true,
      lastSeenAtUtc: json['lastSeenAtUtc'] != null
          ? DateTime.parse(json['lastSeenAtUtc'] as String)
          : DateTime.now().toUtc(),
      revokedAtUtc: json['revokedAtUtc'] != null
          ? DateTime.parse(json['revokedAtUtc'] as String)
          : null,
    );
  }
}

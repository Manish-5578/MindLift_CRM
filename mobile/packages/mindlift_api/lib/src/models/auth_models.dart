class LoginRequest {
  const LoginRequest({
    required this.email,
    required this.password,
    this.rememberMe = true,
  });

  final String email;
  final String password;
  final bool rememberMe;

  Map<String, dynamic> toJson() => {
        'email': email,
        'password': password,
        'rememberMe': rememberMe,
      };
}

class TokenResponse {
  const TokenResponse({
    required this.accessToken,
    this.refreshToken,
    required this.expiresIn,
    this.tokenType = 'Bearer',
    this.tenantId,
    this.userId,
    this.role,
    this.fullName,
    this.email,
  });

  final String accessToken;
  final String? refreshToken;
  final int expiresIn;
  final String tokenType;
  final String? tenantId;
  final String? userId;
  final String? role;
  final String? fullName;
  final String? email;

  factory TokenResponse.fromJson(Map<String, dynamic> json) {
    return TokenResponse(
      accessToken: json['accessToken'] as String? ?? json['token'] as String? ?? '',
      refreshToken: json['refreshToken'] as String?,
      expiresIn: (json['expiresIn'] as num?)?.toInt() ?? 3600,
      tokenType: json['tokenType'] as String? ?? 'Bearer',
      tenantId: json['tenantId'] as String?,
      userId: json['userId'] as String?,
      role: json['role'] as String?,
      fullName: json['fullName'] as String?,
      email: json['email'] as String?,
    );
  }

  Map<String, dynamic> toJson() => {
        'accessToken': accessToken,
        if (refreshToken != null) 'refreshToken': refreshToken,
        'expiresIn': expiresIn,
        'tokenType': tokenType,
        if (tenantId != null) 'tenantId': tenantId,
        if (userId != null) 'userId': userId,
        if (role != null) 'role': role,
        if (fullName != null) 'fullName': fullName,
        if (email != null) 'email': email,
      };
}

class RefreshTokenRequest {
  const RefreshTokenRequest({required this.refreshToken});
  final String refreshToken;

  Map<String, dynamic> toJson() => {'refreshToken': refreshToken};
}


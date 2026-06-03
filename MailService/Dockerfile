# Первый этап - Сборка образа со скачиванием всех пакетов, компиляцией файлов и удалением ненужного после завершения
# .NET 10 SDK на базе Alpine Linux
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
#
WORKDIR /src
#
# Скопировать файл зависимостей и все файлы проекта
COPY ["MailService/MailService.csproj", "MailService/"]
COPY ["SigmaMessenger.Shared/SigmaMessenger.Shared.csproj", "SigmaMessenger.Shared/"]
# Скачать все нужные NuGet пакеты
RUN dotnet restore "MailService/MailService.csproj"
#
# Копируем весь исходный код
COPY . .
#
# Переходим в папку MailService
WORKDIR /src/MailService
#
# Сборка и публикация приложения
# -c Release - оптимизированная сборка
# -o /app/publish - выходная директория
# --self-contained false - используем системный runtime (меньше размер образа)
RUN dotnet publish "MailService.csproj" \
    -c Release \
    -o /app/publish \
    --self-contained false \
    -p:PublishSingleFile=false
#
# Второй этап - Установка утилит нужных для эксплуатации, копирование скомпилированных файлов сохраненных в первом этапе,запуск сервиса
# Легковесный образ только с ASP.NET Core Runtime 10.0
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
#
# Получение порта из докеркомпоуза
ARG SERVICE_PORT
#
# Установка часового пояса. Для корректной записи логов и временных меток в базе
RUN apk add --no-cache tzdata icu-libs wget krb5-libs
ENV TZ=Asia/Yekaterinburg
#
# В образах .NET 8+ уже есть встроенный пользователь "app" (UID 1654), поэтому пользоваеля не создаем
#
WORKDIR /app
#
# Копируем скомпилированное приложение
COPY --from=build /app/publish .
#
# Меняем владельца файлов на пользователя "app"
RUN chown -R app:app /app
#
# Переключаемся на этого непривилегированного пользователя
USER app
#
# Признак что код выполняется внутри контейнера
ENV DOTNET_RUNNING_IN_CONTAINER=true
# Включить механизм глобализации, который форматирует форматы дат для разных регионов и тп
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
#
# Показываем, что контейнер слушает порт ${SERVICE_PORT}
EXPOSE ${SERVICE_PORT}
#
# Проверка работоспособности контейнера
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:${SERVICE_PORT}/health || exit 1
#
# Запускаем приложение
ENTRYPOINT ["dotnet", "MailService.dll"]


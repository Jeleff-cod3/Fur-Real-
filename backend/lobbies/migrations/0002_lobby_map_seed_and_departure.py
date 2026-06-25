# Generated manually for lobby seed control and leave tracking.
from django.conf import settings
from django.db import migrations, models
import django.db.models.deletion


class Migration(migrations.Migration):
    dependencies = [
        migrations.swappable_dependency(settings.AUTH_USER_MODEL),
        ("lobbies", "0001_initial"),
    ]

    operations = [
        migrations.AddField(
            model_name="lobby",
            name="map_seed",
            field=models.IntegerField(default=12345),
        ),
        migrations.CreateModel(
            name="LobbyDeparture",
            fields=[
                ("id", models.BigAutoField(auto_created=True, primary_key=True, serialize=False, verbose_name="ID")),
                ("left_at", models.DateTimeField(auto_now_add=True)),
                (
                    "lobby",
                    models.ForeignKey(
                        on_delete=django.db.models.deletion.CASCADE,
                        related_name="departures",
                        to="lobbies.lobby",
                    ),
                ),
                (
                    "user",
                    models.ForeignKey(
                        on_delete=django.db.models.deletion.CASCADE,
                        related_name="lobby_departures",
                        to=settings.AUTH_USER_MODEL,
                    ),
                ),
            ],
            options={
                "unique_together": {("lobby", "user")},
            },
        ),
    ]
